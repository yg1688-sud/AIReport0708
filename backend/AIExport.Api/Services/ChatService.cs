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
    public async Task<string> SendMessageAsync(Guid sessionId, string content)
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
            Content = content
        });

        // 检测"确认"关键词
        if (content.Trim().Equals("确认", StringComparison.OrdinalIgnoreCase)
            || content.Contains("确认需求"))
        {
            await _db.SaveChangesAsync();
            return await TryConfirmAsync(sessionId);
        }

        // 收集可用列名
        var columns = new List<string>();
        foreach (var f in session.Batch.Files.Where(f => f.ParseStatus == ParseStatus.Ready))
        {
            if (f.ColumnHeaders is null) continue;
            columns.AddRange(JsonSerializer.Deserialize<List<string>>(f.ColumnHeaders) ?? new());
        }
        var uniqueCols = columns.Distinct().ToArray();

        // 尝试 LLM → 失败则降级规则引擎
        string reply;
        if (_llm is not null)
        {
            var llmResult = await _llm.ChatAsync(content, uniqueCols);
            if (llmResult is not null)
            {
                reply = BuildLlmReply(llmResult, uniqueCols);
            }
            else
            {
                reply = BuildRuleEngineReply(content, uniqueCols);
            }
        }
        else
        {
            reply = BuildRuleEngineReply(content, uniqueCols);
        }

        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = sessionId,
            Sender = MessageSender.System,
            Content = reply
        });

        await _db.SaveChangesAsync();
        return reply;
    }

    /// <summary>
    /// 确认需求，创建 AnalysisRequirement 并返回 Task ID
    /// </summary>
    public async Task<(bool Success, Guid TaskId)> ConfirmRequirementsAsync(Guid sessionId)
    {
        var session = await _db.AnalysisSessions
            .Include(s => s.Batch).ThenInclude(b => b.Files)
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == sessionId)
            ?? throw new InvalidOperationException("会话不存在");

        // 从对话中提取需求参数
        var userMessages = session.Messages
            .Where(m => m.Sender == MessageSender.User)
            .Select(m => m.Content)
            .ToList();

        if (userMessages.Count == 0)
            return (false, Guid.Empty);

        // 简化需求提取：合并用户消息作为需求描述
        var fullRequest = string.Join("; ", userMessages);
        var dimensions = ExtractKeywords(fullRequest, new[] { "地区", "时间", "产品", "类别", "日期", "部门", "渠道" });
        var metrics = ExtractKeywords(fullRequest, new[] { "销售额", "利润", "数量", "金额", "收入", "成本", "均价" });
        var chartTypes = ExtractKeywords(fullRequest, new[] { "柱状图", "折线图", "饼图", "趋势图" });

        // 收集列名
        var columns = new List<string>();
        foreach (var f in session.Batch.Files.Where(f => f.ParseStatus == ParseStatus.Ready))
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
            ConfirmedAt = DateTime.UtcNow
        };

        _db.AnalysisRequirements.Add(requirement);
        session.SessionStatus = SessionStatus.Confirmed;
        session.ConfirmedAt = DateTime.UtcNow;

        // 创建报告记录
        var report = new AnalysisReport
        {
            SessionId = sessionId,
            OriginalFileName = session.Batch.Files.FirstOrDefault()?.OriginalName ?? "unknown",
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
        var parts = new List<string>();
        if (result.Dimensions.Count > 0) parts.Add($"分析维度: {string.Join(", ", result.Dimensions)}");
        if (result.Metrics.Count > 0) parts.Add($"分析指标: {string.Join(", ", result.Metrics)}");
        if (result.ChartTypes.Count > 0) parts.Add($"图表类型: {string.Join(", ", result.ChartTypes)}");

        var reply = string.Join("；", parts);
        if (!string.IsNullOrEmpty(result.FollowUpQuestion))
            reply += $"\n\n{result.FollowUpQuestion}";
        else
            reply += "\n\n需求已明确，请输入【确认】生成报告。";

        return reply;
    }

    private static string BuildRuleEngineReply(string userInput, string[] columns)
    {
        // 规则引擎：检查用户输入中引用的列名是否存在于数据中
        var mentioned = columns.Where(c => userInput.Contains(c, StringComparison.OrdinalIgnoreCase)).ToList();
        var notFound = columns.Where(c => userInput.Contains(c, StringComparison.OrdinalIgnoreCase)).ToList();
        // 检查用户是否提到了不存在的列
        var words = userInput.Split(' ', '，', '、', '的', '和', '与');
        var unknownColumns = words
            .Where(w => w.Length >= 2 && !columns.Any(c => c.Equals(w, StringComparison.OrdinalIgnoreCase)))
            .Where(w => w.Length <= 10)
            .Take(3)
            .ToList();

        if (unknownColumns.Count > 0 && !userInput.Contains("确认"))
        {
            var colList = string.Join("、", columns.Take(5));
            return $"您的数据中未包含'{unknownColumns[0]}'字段，请从以下可用列中选择：{colList}";
        }

        return $"好的，我将为您分析。您还希望调整哪些维度或指标？可用列：{string.Join("、", columns.Take(5))}。满意后请输入【确认】生成报告。";
    }

    private static List<string> ExtractKeywords(string text, string[] keywords)
    {
        return keywords.Where(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
