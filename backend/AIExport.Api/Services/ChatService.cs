using System.Text.Json;
using AIExport.Api.Data;
using AIExport.Api.Infrastructure;
using AIExport.Api.Models.Entities;
using AIExport.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AIExport.Api.Services;

/// <summary>
/// 聊天服务：混合模式（LLM + 规则引擎）引导用户确认数据分析需求
/// </summary>
public class ChatService
{
    private readonly AppDbContext _db;
    private readonly LlmClient? _llm;

    public ChatService(AppDbContext db, LlmClient? llm = null)
    {
        _db = db;
        _llm = llm;
    }

    /// <summary>
    /// 启动聊天会话（文件就绪后调用）
    /// </summary>
    public async Task<(AnalysisSession Session, string FirstMessage)> StartSessionAsync(
        Guid batchId, Strategy strategy, Guid? templateId)
    {
        var batch = await _db.UploadBatches
            .Include(b => b.Files)
            .FirstOrDefaultAsync(b => b.Id == batchId)
            ?? throw new InvalidOperationException("批次不存在");

        var mode = templateId.HasValue ? SessionMode.Template : SessionMode.Chat;
        var session = new AnalysisSession
        {
            BatchId = batchId,
            TemplateId = templateId,
            SessionStatus = SessionStatus.Chatting,
            Mode = mode,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };
        _db.AnalysisSessions.Add(session);

        // 收集文件列信息用于引导消息
        var files = batch.Files.Where(f => f.ParseStatus == ParseStatus.Ready).ToList();
        var allColumns = new HashSet<string>();
        foreach (var f in files)
        {
            if (f.ColumnHeaders is null) continue;
            foreach (var col in JsonSerializer.Deserialize<List<string>>(f.ColumnHeaders) ?? new())
                allColumns.Add(col);
        }

        var firstMsg = mode == SessionMode.Template
            ? $"已选择模版模式。共 {files.Count} 个文件就绪，可直接生成报告。"
            : $"我已读取您的数据（{batch.TotalFiles} 个文件），检测到以下列：{string.Join("、", allColumns)}。请告诉我您希望进行哪些分析？例如：销售趋势、分类汇总、对比分析等。";

        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id,
            Sender = MessageSender.System,
            Content = firstMsg
        });

        await _db.SaveChangesAsync();
        return (session, firstMsg);
    }

    /// <summary>
    /// 处理用户消息并返回系统回复（混合模式）
    /// </summary>
    public async Task<string> SendMessageAsync(Guid sessionId, string content, string? model = null)
    {
        try
        {
        var session = await _db.AnalysisSessions
            .Include(s => s.Batch).ThenInclude(b => b.Files)
            .FirstOrDefaultAsync(s => s.Id == sessionId)
            ?? throw new InvalidOperationException("会话不存在");

        // 检查会话超时
        if (DateTime.UtcNow > session.ExpiresAt)
        {
            session.SessionStatus = SessionStatus.Timeout;
            await _db.SaveChangesAsync();
            throw new InvalidOperationException("会话已超时，请重新上传文件开始");
        }

        // 保存用户消息
        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = sessionId,
            Sender = MessageSender.User,
            Content = content ?? ""
        });
        await _db.SaveChangesAsync();

        // 检测"确认"关键词
        if ((content ?? "").Trim().Equals("确认", StringComparison.OrdinalIgnoreCase)
            || (content ?? "").Contains("确认需求"))
        {
            return await TryConfirmAsync(sessionId);
        }

        // 收集可用列名（Batch 非空保护）
        var columns = new List<string>();
        var readyFiles = session.Batch?.Files?.Where(f => f.ParseStatus == ParseStatus.Ready) ?? Array.Empty<UploadedFile>();
        foreach (var f in readyFiles)
        {
            if (f.ColumnHeaders is null) continue;
            columns.AddRange(JsonSerializer.Deserialize<List<string>>(f.ColumnHeaders) ?? new());
        }
        var uniqueCols = columns.Distinct().ToArray();

        // 尝试 LLM → 失败则降级规则引擎
        // 获取对话历史（最近10轮）
        var historyMessages = await _db.ChatMessages
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.Timestamp)
            .ToListAsync();
        historyMessages = historyMessages.TakeLast(20).ToList();
        var history = historyMessages.Select(m => (
            role: m.Sender == MessageSender.User ? "user" : "assistant",
            content: m.Content.Split("<!--reasoning:")[0].Trim()
        )).ToList();

        string reply;
        string? reasoning = null;
        if (_llm is not null)
        {
            var llmResult = await _llm.ChatAsync(content, uniqueCols, model, history);
            if (llmResult is not null)
            {
                Console.WriteLine($"[ChatService] Using DeepSeek V4 for: {content}");
                reasoning = llmResult.Reasoning;
                reply = BuildLlmReply(llmResult, uniqueCols);
            }
            else
            {
                Console.WriteLine($"[ChatService] DeepSeek unavailable, falling back to rule engine");
                reply = BuildRuleEngineReply(content, uniqueCols);
            }
        }
        else
        {
            reply = BuildRuleEngineReply(content, uniqueCols);
        }

        var fullReply = reply + (reasoning is not null ? $"\n<!--reasoning:{reasoning}-->" : "");
        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = sessionId,
            Sender = MessageSender.System,
            Content = fullReply
        });

        await _db.SaveChangesAsync();
        return fullReply;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ChatService] Error: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            throw;
        }
    }

    /// <summary>
    /// 确认需求，创建 AnalysisRequirement 并返回 Task ID
    /// </summary>
    public async Task<(bool Success, Guid TaskId)> ConfirmRequirementsAsync(Guid sessionId)
    {
        // 检查是否已确认（幂等性保护）
        var existingReport = await _db.AnalysisReports.FirstOrDefaultAsync(r => r.SessionId == sessionId);
        if (existingReport is not null)
            return (true, existingReport.Id);

        var session = await _db.AnalysisSessions
            .Include(s => s.Batch).ThenInclude(b => b.Files)
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == sessionId)
            ?? throw new InvalidOperationException("会话不存在");

        // 从对话中提取需求参数（或从模版中加载）
        if (session.Mode == SessionMode.Template && session.TemplateId.HasValue)
        {
            var template = await _db.AnalysisTemplates.FindAsync(session.TemplateId.Value);
            if (template is null) return (false, Guid.Empty);

            var req = new AnalysisRequirement
            {
                SessionId = sessionId,
                Dimensions = template.Dimensions,
                Metrics = template.Metrics,
                ChartTypes = template.ChartTypes,
                Filters = template.Filters,
                CustomRequirements = template.CustomRequirements,
                ConfirmedAt = DateTime.UtcNow
            };
            _db.AnalysisRequirements.Add(req);
            session.SessionStatus = SessionStatus.Confirmed;
            session.ConfirmedAt = DateTime.UtcNow;

            var rep = new AnalysisReport
            {
                SessionId = sessionId,
                OriginalFileName = "模版生成",
                ReportStatus = ReportStatus.Generating,
                Mode = session.Mode,
                ReportType = template.Strategy == Strategy.Merge ? ReportType.MergeReport : ReportType.SingleFileReport,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };
            _db.AnalysisReports.Add(rep);
            await _db.SaveChangesAsync();
            return (true, rep.Id);
        }

        // 对话模式：取最近 10 条消息（约5轮对话）提取需求
        var recentMessages = session.Messages
            .OrderBy(m => m.Timestamp)
            .TakeLast(10)
            .ToList();

        var userMessages = recentMessages
            .Where(m => m.Sender == MessageSender.User)
            .Select(m => m.Content)
            .ToList();

        if (userMessages.Count == 0)
            return (false, Guid.Empty);

        var fullRequest = string.Join("\n", userMessages);
        List<string> dimensions, metrics, chartTypes;

        // 尝试用 LLM 解析对话提取结构化需求
        if (_llm is not null)
        {
            var parsePrompt = $"用户与数据分析助手的完整对话如下：\n{fullRequest}\n\n" +
                "请从对话中提取用户最终确认的分析需求，返回JSON（不要包含其他文字）：\n" +
                "{\"dimensions\":[\"分析维度\"],\"metrics\":[\"分析指标\"],\"chartTypes\":[\"bar/line/pie\"]}\n\n" +
                "规则：dimensions是分组依据（如地区、时间），metrics是计算指标（如销售额、数量），chartTypes是图表类型。如果对话中没有明确，请根据上下文合理推断。";

            var cols = new List<string>();
            foreach (var f in session.Batch?.Files?.Where(f => f.ParseStatus == ParseStatus.Ready) ?? Array.Empty<UploadedFile>())
            { if (f.ColumnHeaders is not null) cols.AddRange(JsonSerializer.Deserialize<List<string>>(f.ColumnHeaders) ?? new()); }

            var parseResult = await _llm.ChatAsync(parsePrompt, cols.Distinct().ToArray());
            if (parseResult is not null)
            {
                dimensions = parseResult.Dimensions ?? new();
                metrics = parseResult.Metrics ?? new();
                chartTypes = parseResult.ChartTypes ?? new();
            }
            else
            {
                dimensions = ExtractKeywords(fullRequest, new[] { "地区", "时间", "产品", "类别", "日期", "部门", "渠道", "门店", "名称" });
                metrics = ExtractKeywords(fullRequest, new[] { "销售额", "利润", "数量", "金额", "收入", "成本", "均价", "单价" });
                chartTypes = ExtractKeywords(fullRequest, new[] { "柱状图", "折线图", "饼图", "趋势图" });
            }
        }
        else
        {
            dimensions = ExtractKeywords(fullRequest, new[] { "地区", "时间", "产品", "类别", "日期", "部门", "渠道", "门店", "名称" });
            metrics = ExtractKeywords(fullRequest, new[] { "销售额", "利润", "数量", "金额", "收入", "成本", "均价", "单价" });
            chartTypes = ExtractKeywords(fullRequest, new[] { "柱状图", "折线图", "饼图", "趋势图" });
        }

        // 收集列名
        var columns = new List<string>();
        var readyFiles = session.Batch?.Files?.Where(f => f.ParseStatus == ParseStatus.Ready) ?? Array.Empty<UploadedFile>();
        foreach (var f in readyFiles)
        {
            if (f.ColumnHeaders is null) continue;
            columns.AddRange(JsonSerializer.Deserialize<List<string>>(f.ColumnHeaders) ?? new());
        }

        var requirement = new AnalysisRequirement
        {
            SessionId = sessionId,
            Dimensions = JsonSerializer.Serialize(dimensions.Count > 0 ? dimensions.ToArray() : new[] { "未指定维度" }),
            Metrics = JsonSerializer.Serialize(metrics.Count > 0 ? metrics.ToArray() : new[] { "未指定指标" }),
            ChartTypes = JsonSerializer.Serialize(chartTypes.Count > 0 ? chartTypes.ToArray() : new[] { "bar" }),
            CustomRequirements = fullRequest, // 保存用户完整需求描述
            ConfirmedAt = DateTime.UtcNow
        };

        _db.AnalysisRequirements.Add(requirement);
        session.SessionStatus = SessionStatus.Confirmed;
        session.ConfirmedAt = DateTime.UtcNow;

        // 创建报告记录
        var report = new AnalysisReport
        {
            SessionId = sessionId,
            OriginalFileName = readyFiles.FirstOrDefault()?.OriginalName ?? "unknown",
            ReportStatus = ReportStatus.Generating,
            Mode = session.Mode,
            ReportType = session.Batch.Strategy == Strategy.Merge ? ReportType.MergeReport : ReportType.SingleFileReport,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        _db.AnalysisReports.Add(report);

        await _db.SaveChangesAsync();

        var taskId = report.Id; // Task ID = Report ID
        return (true, taskId);
    }

    public async Task<List<ChatMessage>> GetMessagesAsync(Guid sessionId)
    {
        return await _db.ChatMessages
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.Timestamp)
            .ToListAsync();
    }

    private async Task<string> TryConfirmAsync(Guid sessionId)
    {
        var (success, taskId) = await ConfirmRequirementsAsync(sessionId);
        if (success)
            return $"需求已确认。任务ID: {taskId}，正在生成报告...";
        return "请先描述您的分析需求，然后再确认。例如：按地区分析销售额趋势。";
    }

    private static string BuildLlmReply(LlmResponse result, string[] columns)
    {
        // 检测并解析原始 JSON 回复（Thinking 模式可能输出非结构化 JSON）
        if (result.Dimensions is null && result.Metrics is null && result.FollowUpQuestion is null)
        {
            // LLM 可能返回了原始 JSON 在 Reasoning 中，尝试解析
            var suggestions = SuggestAnalysis(columns);
            return suggestions;
        }

        var parts = new List<string>();
        var dims = result.Dimensions ?? new();
        var mets = result.Metrics ?? new();
        var charts = result.ChartTypes ?? new();
        if (dims.Count > 0) parts.Add($"分析维度: {string.Join(", ", dims)}");
        if (mets.Count > 0) parts.Add($"分析指标: {string.Join(", ", mets)}");
        if (charts.Count > 0) parts.Add($"图表类型: {string.Join(", ", charts)}");

        var reply = string.Join("；", parts);
        if (!string.IsNullOrEmpty(result.FollowUpQuestion))
        {
            reply = (reply.Length > 0 ? reply + "\n\n" : "") + result.FollowUpQuestion;
        }
        else if (dims.Count > 0 && mets.Count > 0)
        {
            reply = (reply.Length > 0 ? reply + "\n\n" : "") + "需求已明确，请输入【确认】生成报告。";
        }
        else
        {
            // LLM 没有提取到分析参数 → 基于列名给出引导建议
            var suggestions = SuggestAnalysis(columns);
            reply = (reply.Length > 0 ? reply + "\n\n" : "") + suggestions;
        }

        // 附加思维链（如有）
        if (!string.IsNullOrEmpty(result.Reasoning))
            reply += $"<!--reasoning:{result.Reasoning}-->";

        return reply;
    }

    private static string SuggestAnalysis(string[] columns)
    {
        if (columns is null || columns.Length == 0) return "请问您想对数据做哪些分析？";
        var numeric = columns.Where(c => c.Contains("金额") || c.Contains("数量") || c.Contains("价") || c.Contains("额") || c.Contains("费")).Take(3).ToList();
        var textCols = columns.Where(c => c.Contains("名称") || c.Contains("门店") || c.Contains("地区") || c.Contains("部门") || c.Contains("类别")).Take(3).ToList();
        var dateCols = columns.Where(c => c.Contains("时间") || c.Contains("日期")).Take(2).ToList();

        var tips = new List<string>();
        if (numeric.Count > 0 && textCols.Count > 0)
            tips.Add($"例如：按{textCols[0]}统计{numeric[0]}的分布");
        if (dateCols.Count > 0 && numeric.Count > 0)
            tips.Add($"按{dateCols[0]}查看{numeric[0]}的变化趋势");
        if (numeric.Count >= 2)
            tips.Add($"对比{numeric[0]}与{numeric[1]}的关系");

        if (tips.Count > 0)
            return "您的需求我还不太确定。以下是一些分析建议：\n- " + string.Join("\n- ", tips) + "\n\n请更具体地描述您想要的分析，例如：按XX统计XX的分布。";
        else
            return $"您的数据包含以下列：{string.Join("、", columns.Take(8))}。请告诉我您想分析哪些指标？";
    }

    private static string BuildRuleEngineReply(string userInput, string[] columns)
    {
        var input = (userInput ?? "").Trim();
        if (columns is null || columns.Length == 0)
            return "未检测到可分析的数据列，请确认数据格式规范。";

        // 问候语检测
        var greetings = new[] { "你好", "hello", "hi", "嗨", "在吗", "您好" };
        if (greetings.Any(g => input.StartsWith(g, StringComparison.OrdinalIgnoreCase)) && input.Length <= 5)
        {
            var sampleCols = string.Join("、", columns.Take(5));
            return $"您好！我已读取了您的数据，共包含 {columns.Length} 个数据列：{sampleCols}等。请告诉我您想进行哪些分析？例如：按产品统计销售额、分析时间趋势、对比不同地区的销量等。";
        }

        // 检查是否有具体分析关键词
        var analysisKeywords = new[] { "分析", "统计", "对比", "趋势", "汇总", "分布", "计算", "图表", "柱状图", "折线图", "饼图" };
        var hasAnalysisIntent = analysisKeywords.Any(k => input.Contains(k));

        if (!hasAnalysisIntent && input.Length < 10)
        {
            var sampleCols = string.Join("、", columns.Take(5));
            return $"请问您想对数据做哪些分析呢？例如：\n- 按某个维度统计汇总（可用维度：{sampleCols}）\n- 查看数据的时间趋势\n- 对比不同类别的数据分布\n请描述您的需求，我会帮您配置分析参数。";
        }

        // 检查未知列
        var words = input.Split(' ', '，', '、', '的', '和', '与', '按', '对', '在');
        var unknownColumns = words
            .Where(w => w.Length >= 2 && !columns.Any(c => c.Equals(w, StringComparison.OrdinalIgnoreCase)))
            .Where(w => !analysisKeywords.Contains(w) && !greetings.Contains(w))
            .Take(3).ToList();

        if (unknownColumns.Count > 0)
        {
            var colList = string.Join("、", columns.Take(8));
            return $"您的数据中未包含'{unknownColumns[0]}'字段。可用列包括：{colList}等。请基于这些列重新描述您的分析需求。";
        }

        return $"好的，我理解您的需求。可用列：{string.Join("、", columns.Take(6))}。请补充更多细节，或输入【确认】生成报告。";
    }

    private static List<string> ExtractKeywords(string text, string[] keywords)
    {
        return keywords.Where(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
