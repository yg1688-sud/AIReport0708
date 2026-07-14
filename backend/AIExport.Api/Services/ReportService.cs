using System.Text.Json;
using System.Text.RegularExpressions;
using AIExport.Api.Data;
using AIExport.Api.Infrastructure;
using AIExport.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIExport.Api.Services;

public record GenerateResult(ReportStatus Status, string? Chapters, string? ErrorMessage);
public record ProgressEvent(string Stage, int Percent, string Message);

public class ReportService
{
    private readonly AppDbContext _db;
    private readonly StorageService _storage;
    private readonly FileParser _parser;
    private readonly PdfGenerator _pdf;
    public ReportService(AppDbContext db, StorageService storage, FileParser parser, PdfGenerator pdf)
    { _db = db; _storage = storage; _parser = parser; _pdf = pdf; }

    public async Task<GenerateResult> GenerateAsync(Guid reportId, CancellationToken ct = default)
    {
        var report = await _db.AnalysisReports
            .Include(r => r.Session).ThenInclude(s => s.Batch).ThenInclude(b => b.Files)
            .Include(r => r.Session).ThenInclude(s => s.Requirement)
            .FirstOrDefaultAsync(r => r.Id == reportId, ct)
            ?? throw new InvalidOperationException("报告不存在");

        try
        {
            report.ReportStatus = ReportStatus.Generating; await _db.SaveChangesAsync(ct);
            NotifyProgress(reportId, "cleaning", 10, "正在清洗数据...");

            var req = report.Session.Requirement;
            var reqDims = req is not null ? JsonSerializer.Deserialize<List<string>>(req.Dimensions) ?? new() : new();
            var reqMets = req is not null ? JsonSerializer.Deserialize<List<string>>(req.Metrics) ?? new() : new();
            var reqCharts = req is not null ? JsonSerializer.Deserialize<List<string>>(req.ChartTypes) ?? new() : new();

            var allRows = new List<List<string>>();
            string[]? headers = null; int totalRows = 0;
            foreach (var file in report.Session.Batch.Files.Where(f => f.ParseStatus == ParseStatus.Ready))
            { var r = await _parser.ParseAllAsync(file.StoredPath, file.FileFormat, ct); headers ??= r.ColumnHeaders; allRows.AddRange(r.PreviewRows); totalRows = r.RowCount; }
            var cols = headers ?? Array.Empty<string>();

            NotifyProgress(reportId, "statistics", 40, "正在按需求计算...");
            var stats = CalculateStatistics(allRows, cols);

            var groupedResults = new List<object>();
            if (reqDims.Count > 0 && reqMets.Count > 0 && allRows.Count > 0)
            {
                var dimIdx = cols.Select((c,i) => new{c,i}).FirstOrDefault(x => reqDims.Any(d => x.c.Contains(d)))?.i ?? -1;
                var metricIdx = cols.Select((c,i) => new{c,i}).FirstOrDefault(x => reqMets.Any(m => x.c.Contains(m)))?.i ?? -1;
                if (dimIdx >= 0 && metricIdx >= 0)
                    foreach (var g in allRows.GroupBy(r => r.Count > dimIdx ? r[dimIdx] : "未知"))
                    { var vals = g.Select(r => r.Count > metricIdx && double.TryParse(r[metricIdx], out var v) ? v : 0).ToList(); if (vals.Count > 0) groupedResults.Add(new { dimension = g.Key, count = g.Count(), sum = vals.Sum(), avg = vals.Average() }); }
            }

            NotifyProgress(reportId, "grouping", 70, "正在按需求分组统计...");
            var chartData = new { type = reqCharts, groupedData = groupedResults };

            NotifyProgress(reportId, "computing", 85, "正在生成计算结果...");
            var crossAnalysis = groupedResults.Select(g => { dynamic d = g; return $"{d.dimension}: 计数={d.count}, 合计={d.sum:F2}, 均值={d.avg:F2}"; }).ToList();

            // LLM 理解需求 + 代码执行计算
            var computedResults = new List<string>();
            var analysisText = "";
            var customReq = report.Session.Requirement?.CustomRequirements;

            var colIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < cols.Length; i++) colIndex[cols[i].Trim()] = i;

            // 代码直接计算（正则匹配常见分析模式：去重/过滤/比率）
            computedResults = ComputeCustomRequirements(customReq, allRows, cols);

            // 直接使用计算结果，无需 LLM 额外处理
            if (computedResults.Count > 0)
                analysisText = string.Join("\n", computedResults.Select(r => $"• {r}"));
            else if (crossAnalysis.Count > 0)
                analysisText = string.Join("\n", crossAnalysis);

            NotifyProgress(reportId, "rendering", 95, "正在生成分析报告...");
            var pdfData = BuildReportData(report, cols, stats, totalRows, crossAnalysis, analysisText, computedResults);
            byte[] pdfBytes = Array.Empty<byte>();
            try { pdfBytes = _pdf.Generate(pdfData); }
            catch (Exception ex) { report.ErrorMessage = "PDF导出失败（分析结果仍可查看）: " + ex.Message; }
            string pdfPath = "";
            if (pdfBytes.Length > 0)
            {
                try {
                    var pdfDir = Path.Combine(_storage.GetUserDirectory(report.Session.Batch.UserId), "reports");
                    Directory.CreateDirectory(pdfDir);
                    pdfPath = Path.Combine(pdfDir, $"{reportId}.pdf");
                    await File.WriteAllBytesAsync(pdfPath, pdfBytes, ct);
                }
                catch (Exception ex) { Console.Error.WriteLine($"[PDF] Save failed: {ex.Message}"); pdfPath = ""; }
            }

            var chapters = JsonSerializer.Serialize(new {
                overview = new { rowCount = totalRows, columnCount = cols.Length, stats.MissingRate, report.OriginalFileName },
                statistics = stats.ColumnStats, charts = chartData, crossAnalysis,
                customRequirements = customReq, analysisText, computedResults,
                requirements = new { dimensions = reqDims, metrics = reqMets, chartTypes = reqCharts }
            });

            report.Chapters = chapters; report.PdfPath = string.IsNullOrEmpty(pdfPath) ? null : pdfPath; report.FileSize = pdfBytes.Length;
            report.ReportStatus = ReportStatus.Completed; report.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            NotifyProgress(reportId, "complete", 100, "报告生成完成");
            return new GenerateResult(ReportStatus.Completed, chapters, null);
        }
        catch (Exception ex)
        {
            report.ReportStatus = ReportStatus.Failed; report.ErrorMessage = $"报告生成遇到问题：{ex.Message}";
            await _db.SaveChangesAsync(ct);
            return new GenerateResult(ReportStatus.Failed, null, report.ErrorMessage);
        }
    }

    public async Task<AnalysisReport?> GetReportAsync(Guid reportId)
        => await _db.AnalysisReports.Include(r => r.Session).FirstOrDefaultAsync(r => r.Id == reportId);

    private static StatisticsResult CalculateStatistics(List<List<string>> rows, string[] headers)
    {
        var colStats = new List<ColumnStat>(); int totalCells = 0, missingCells = 0;
        for (int c = 0; c < headers.Length && c < 20; c++)
        {
            var values = new List<double>();
            foreach (var row in rows)
            { totalCells++; if (c < row.Count && double.TryParse(row[c], out var val)) values.Add(val); else missingCells++; }
            if (values.Count > 0) { values.Sort(); colStats.Add(new ColumnStat(headers[c], Math.Round(values.Average(),2), Math.Round(values[values.Count/2],2), Math.Round(values.Min(),2), Math.Round(values.Max(),2), Math.Round(Math.Sqrt(values.Average(v => Math.Pow(v-values.Average(),2))),2))); }
        }
        return new StatisticsResult(colStats, totalCells > 0 ? (double)missingCells / totalCells : 0);
    }

    private static ReportData BuildReportData(AnalysisReport report, string[] headers, StatisticsResult stats, int totalRows, List<string> crossAnalysis, string analysisText, List<string> computedResults)
    {
        var req = report.Session.Requirement;
        var chartTypes = req is not null ? JsonSerializer.Deserialize<List<string>>(req.ChartTypes) ?? new() : new();
        var dims = req is not null ? JsonSerializer.Deserialize<List<string>>(req.Dimensions) ?? new() : new();
        var mets = req is not null ? JsonSerializer.Deserialize<List<string>>(req.Metrics) ?? new() : new();
        var customReq = report.Session.Requirement?.CustomRequirements;
        return new ReportData($"数据分析报告 — {report.OriginalFileName}", DateTime.UtcNow,
            new OverviewData(totalRows, headers.Length, stats.MissingRate, report.OriginalFileName),
            stats.ColumnStats.Select(s => new StatisticRow(s.ColumnName, s.Mean, s.Median, s.Min, s.Max, s.StdDev)).ToList(),
            chartTypes, crossAnalysis.Select(c => new CrossRow(string.Join("、", dims), string.Join("、", mets), c)).ToList(),
            analysisText, computedResults, customReq);
    }

    /// <summary>
    /// 从用户需求文本中智能提取：分组列、去重列、过滤列+值、比率，并执行计算。
    /// 不依赖特定关键词顺序，对 LLM 的各种表达方式都兼容。
    /// </summary>
    public static List<string> ComputeCustomRequirements(string? requirements, List<List<string>> allRows, string[] cols)
    {
        var results = new List<string>();
        if (string.IsNullOrEmpty(requirements) || allRows.Count == 0) return results;
        var colIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < cols.Length; i++) colIndex[cols[i].Trim()] = i;

        // === 1. 找出需求文本中出现的所有数据列 ===
        var mentionedCols = colIndex.Keys.Where(c => requirements.Contains(c)).ToList();

        // === 2. 找分组列：只匹配"分组"关键词附近的列，不降级 ===
        string? groupCol = null; int groupIdx = -1;
        var groupKwPos = requirements.IndexOf("分组");
        if (groupKwPos >= 0) {
            foreach (var c in mentionedCols) {
                var pos = requirements.IndexOf(c);
                if (Math.Abs(pos - groupKwPos) < 15) { groupCol = c; groupIdx = colIndex[c]; break; }
            }
        }

        // === 3. 找去重列：列名 + 去重/不重复/计数 关键词（在40字符内） ===
        string? dedupCol = null; int dedupIdx = -1;
        var dedupKw = new[] { "去重", "不重复", "唯一" };
        foreach (var c in mentionedCols)
        {
            var pos = requirements.IndexOf(c);
            var found = false;
            foreach (var kw in dedupKw) {
                var kwPos = requirements.IndexOf(kw);
                if (kwPos >= 0 && Math.Abs(kwPos - pos) < 40) { found = true; break; }
            }
            if (found) { dedupCol = c; dedupIdx = colIndex[c]; break; }
        }

        // === 4. 找过滤列+值：非去重列 + 附近有数字 ===
        string? filterCol = null; string? filterVal = null; int filterIdx = -1;
        foreach (var c in mentionedCols)
        {
            if (c == dedupCol || c == groupCol) continue;
            var pos = requirements.IndexOf(c);
            // 查找列名前后30字符内的数字
            var start = Math.Max(0, pos - 20);
            var len = Math.Min(requirements.Length - start, c.Length + 40);
            var near = requirements.Substring(start, len);
            var numMatch = Regex.Match(near, @"(\d{2,})");
            if (numMatch.Success)
            {
                filterCol = c; filterVal = numMatch.Groups[1].Value;
                filterIdx = colIndex[c]; break;
            }
        }

        // === 5. 执行计算 ===
        if (groupIdx < 0 && dedupIdx >= 0 && (requirements.Contains("按") || requirements.Contains("各") || requirements.Contains("每个")))
        {
            // 检测到分组意图但未找到"分组"关键词 → 提示用户重新确认
            results.Add("未在需求中找到明确的分组关键词（请确保需求描述中包含\"分组\"一词），无法按维度分组。请重新确认需求。");
        }
        else if (groupIdx >= 0 && dedupIdx >= 0)
        {
            // 分组计算
            var groups = allRows.Where(r => r.Count > groupIdx && !string.IsNullOrWhiteSpace(r[groupIdx]))
                .GroupBy(r => r[groupIdx].Trim()).OrderBy(g => g.Key);
            results.Add($"分组列: {groupCol} | 去重列: {dedupCol} | 过滤: {filterCol}={filterVal}");
            results.Add("---");
            foreach (var g in groups)
            {
                var rows = g.ToList();
                var totalDedup = rows.Where(r => r.Count > dedupIdx && !string.IsNullOrWhiteSpace(r[dedupIdx]))
                    .Select(r => r[dedupIdx].Trim()).Distinct().Count();
                int filteredDedup = 0;
                if (filterIdx >= 0 && filterVal != null)
                {
                    var filtered = rows.Where(r => r.Count > filterIdx && r[filterIdx].Trim().Equals(filterVal, StringComparison.OrdinalIgnoreCase)).ToList();
                    filteredDedup = filtered.Where(r => r.Count > dedupIdx && !string.IsNullOrWhiteSpace(r[dedupIdx]))
                        .Select(r => r[dedupIdx].Trim()).Distinct().Count();
                }
                var ratio = totalDedup > 0 ? (double)filteredDedup / totalDedup * 100 : 0;
                results.Add($"{g.Key}: 订单总数={totalDedup}, 关联任务品订单数={filteredDedup}, 关联率={ratio:F2}%");
            }
        }
        else if (dedupIdx >= 0)
        {
            // 无分组，全局计算
            var totalDedup = allRows.Where(r => r.Count > dedupIdx && !string.IsNullOrWhiteSpace(r[dedupIdx]))
                .Select(r => r[dedupIdx].Trim()).Distinct().Count();
            results.Add($"{dedupCol}去重计数: {totalDedup}");
            if (filterIdx >= 0 && filterVal != null)
            {
                var filtered = allRows.Where(r => r.Count > filterIdx && r[filterIdx].Trim().Equals(filterVal, StringComparison.OrdinalIgnoreCase)).ToList();
                var fcnt = filtered.Where(r => r.Count > dedupIdx && !string.IsNullOrWhiteSpace(r[dedupIdx]))
                    .Select(r => r[dedupIdx].Trim()).Distinct().Count();
                results.Add($"过滤{filterCol}={filterVal}后{dedupCol}去重计数: {fcnt}");
                if (totalDedup > 0) results.Add($"比率: {(double)fcnt / totalDedup * 100:F2}%");
            }
        }
        return results;
    }

    // === 以下为旧方法，保留供参考 ===
    public static List<string> ComputeCustomRequirements_Legacy(string? requirements, List<List<string>> allRows, string[] cols)
    {
        var results = new List<string>();
        if (string.IsNullOrEmpty(requirements) || allRows.Count == 0) return results;
        var colIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < cols.Length; i++) colIndex[cols[i].Trim()] = i;

        // 检测分组维度（如"按片区分组" / "根据片区" / "各片区"）
        var groupPattern = new Regex(@"(?:按|根据)\s*(\S+?)\s*(?:分组|维度|进行|分别统计|统计)|各\s*(\S+?)\s*(?:分别|单独)", RegexOptions.IgnoreCase);
        var groupMatch = groupPattern.Match(requirements);
        string? groupCol = null;
        int groupIdx = -1;
        if (groupMatch.Success)
        {
            var gc = (groupMatch.Groups[1].Success ? groupMatch.Groups[1] : groupMatch.Groups[2]).Value.Trim();
            groupCol = colIndex.Keys.FirstOrDefault(k => k.Contains(gc) || gc.Contains(k));
            if (groupCol != null) colIndex.TryGetValue(groupCol, out groupIdx);
        }
        // 正则未匹配时，从列名中查找需求文本里提到的维度列
        if (groupIdx < 0)
        {
            foreach (var c in colIndex.Keys)
            {
                if (requirements.Contains(c) && (requirements.Contains("分组") || requirements.Contains("各组") || requirements.Contains("分别") || requirements.Contains("每个") || requirements.Contains("维度") || requirements.Contains("片区")))
                {
                    groupCol = c; groupIdx = colIndex[c]; break;
                }
            }
        }

        // 提取去重列名（匹配"订单号去重"、"对订单号进行去重"等表达）
        var dp = new Regex(@"(?:对|将)?\s*(\S+?)\s*(?:列)?\s*(?:进行|根据|按)?\s*去重(?:计数|统计)?", RegexOptions.IgnoreCase);
        var dm = dp.Match(requirements);
        var simpleDedupCol = dm.Success ? dm.Groups[1].Value.Trim() : "";
        if (string.IsNullOrEmpty(simpleDedupCol))
        {
            // 兜底：从需求中找到所有列名中出现"去重"关键词最近的列
            foreach (var c in colIndex.Keys)
            {
                var idx = requirements.IndexOf(c);
                if (idx >= 0 && requirements.IndexOf("去重", idx) - idx < 30)
                { simpleDedupCol = c; break; }
            }
        }
        int sdci = -1;
        var sdcol = colIndex.Keys.FirstOrDefault(k => k.Contains(simpleDedupCol) || simpleDedupCol.Contains(k));
        if (sdcol != null) colIndex.TryGetValue(sdcol, out sdci);

        // 提取过滤参数
        string? filterCol = null, filterVal = null, dedupCol = null;
        // 匹配过滤条件：列名附近出现数字（如"商品ERPID=2520941"、"筛选商品ERPID值为2520941"）
        var fm = new Regex(@"过滤\s*(\S+?)\s*[=＝：:为是]\s*(\d{2,})", RegexOptions.IgnoreCase).Match(requirements);
        if (!fm.Success) fm = new Regex(@"筛选\s*(\S+?)\s*[=＝：:为是]?\s*值?[为是]?\s*(\d{2,})", RegexOptions.IgnoreCase).Match(requirements);
        if (!fm.Success) fm = new Regex(@"根据\s*(\d{2,})\s*过滤\s*(\S+?)", RegexOptions.IgnoreCase).Match(requirements);
        if (fm.Success && fm.Groups.Count >= 3)
        {
            if (decimal.TryParse(fm.Groups[1].Value, out _))
            { filterCol = fm.Groups[2].Value.Trim(); filterVal = fm.Groups[1].Value.Trim(); }
            else { filterCol = fm.Groups[1].Value.Trim(); filterVal = fm.Groups[2].Value.Trim(); }
            // 提取去重列
            var ddMatch = Regex.Match(requirements, @"(?:然后|再|对)\s*(\S+?)\s*(?:列)?\s*去重");
            if (ddMatch.Success) dedupCol = ddMatch.Groups[1].Value.Trim();
        }
        if (fm.Success)
        {
            filterCol = fm.Groups[1].Value.Trim(); filterVal = fm.Groups[2].Value.Trim(); dedupCol = fm.Groups[3].Value.Trim();
            // 修正：如果 filterCol 是数字或 filterVal 是列名，交换
            if (decimal.TryParse(filterCol, out _) || colIndex.ContainsKey(filterCol)) {
                if (decimal.TryParse(filterVal, out _) || !colIndex.ContainsKey(filterVal)) {
                    // 都不对，尝试从需求中提取
                    filterCol = null; filterVal = null;
                }
            }
        }
        // 兜底：在需求文本中查找数字（排除去重列后，找列名前后30字符内的第一个数字）
        if (string.IsNullOrEmpty(filterCol) || string.IsNullOrEmpty(filterVal))
        {
            foreach (var c in colIndex.Keys)
            {
                if (c == simpleDedupCol || c == dedupCol) continue;
                var idx = requirements.IndexOf(c);
                if (idx < 0) continue;
                // 取列名前后共30个字符的范围
                var start = Math.Max(0, idx - 15);
                var len = Math.Min(requirements.Length - start, c.Length + 30);
                var near = requirements.Substring(start, len);
                var numMatch = Regex.Match(near, @"(\d{2,})");
                if (numMatch.Success)
                {
                    filterCol = c; filterVal = numMatch.Groups[1].Value; break;
                }
            }
        }

        // 提取去重列名
        // 查找列索引
        var fcol = filterCol != null ? colIndex.Keys.FirstOrDefault(k => k.Contains(filterCol) || filterCol.Contains(k)) : null;
        var dcol = dedupCol != null ? colIndex.Keys.FirstOrDefault(k => k.Contains(dedupCol) || dedupCol.Contains(k)) : null;

        int fci = -1, dci = -1;
        if (fcol != null) colIndex.TryGetValue(fcol, out fci);
        if (dcol != null) colIndex.TryGetValue(dcol, out dci);
        // 未找到过滤去重列时，使用简单去重列
        if (dci < 0 && filterCol != null && filterVal != null) { dcol = sdcol; dci = sdci; }

        if (groupIdx >= 0 && sdci >= 0)
        {
            // === 按分组计算 ===
            var groups = allRows.Where(r => r.Count > groupIdx && !string.IsNullOrWhiteSpace(r[groupIdx]))
                .GroupBy(r => r[groupIdx].Trim()).OrderBy(g => g.Key);
            results.Add($"分组列: {groupCol} | 总去重列: {sdcol} | 过滤条件: {fcol}={filterVal} | 过滤去重列: {dcol}");
            results.Add("---");
            foreach (var g in groups)
            {
                var rows = g.ToList();
                var totalDedup = rows.Where(r => r.Count > sdci && !string.IsNullOrWhiteSpace(r[sdci])).Select(r => r[sdci].Trim()).Distinct().Count();
                int filteredDedup = 0;
                if (fci >= 0 && dci >= 0 && filterVal != null)
                {
                    var filtered = rows.Where(r => r.Count > fci && r[fci].Trim().Equals(filterVal, StringComparison.OrdinalIgnoreCase)).ToList();
                    filteredDedup = filtered.Where(r => r.Count > dci && !string.IsNullOrWhiteSpace(r[dci])).Select(r => r[dci].Trim()).Distinct().Count();
                }
                var ratio = totalDedup > 0 ? (double)filteredDedup / totalDedup * 100 : 0;
                results.Add($"{g.Key}: 订单总数={totalDedup}, 关联任务品订单数={filteredDedup}, 关联率={ratio:F2}%");
            }
        }
        else
        {
            // === 不分组，全局计算（原逻辑） ===
            if (fci >= 0 && dci >= 0 && filterVal != null && sdci >= 0)
                ComputeFilteredDedup(filterCol!, filterVal!, dedupCol!, colIndex, allRows, results);

            var dedupResults = MatchDedupPatterns(requirements, colIndex, allRows);
            foreach (var dr in dedupResults) { if (!results.Any(r => r.StartsWith(dr.Split(':')[0]))) results.Add(dr); }

            if (requirements.Contains('/') || requirements.Contains('÷'))
            {
                var numericResults = results.Where(r => { var p = r.Split(':'); return p.Length >= 2 && double.TryParse(p[1].Trim(), out _); }).ToList();
                if (numericResults.Count >= 2)
                {
                    var vals = numericResults.Select(r => double.Parse(r.Split(':')[1].Trim())).OrderBy(v => v).ToList();
                    results.Add($"比率: {vals.First() / vals.Last() * 100:F2}%");
                }
            }
        }
        return results;
    }

    private static void ComputeFilteredDedup(string filterCol, string filterVal, string dedupCol,
        Dictionary<string, int> colIndex, List<List<string>> allRows, List<string> results)
    {

        var fcol = colIndex.Keys.FirstOrDefault(k => k.Contains(filterCol) || filterCol.Contains(k));
        var dcol = colIndex.Keys.FirstOrDefault(k => k.Contains(dedupCol) || dedupCol.Contains(k));
        if (fcol != null && dcol != null && colIndex.TryGetValue(fcol, out var fi) && colIndex.TryGetValue(dcol, out var di))
        {
            var filtered = allRows.Where(r => r.Count > fi && r[fi].Trim().Equals(filterVal, StringComparison.OrdinalIgnoreCase)).ToList();
            var cnt = filtered.Where(r => r.Count > di && !string.IsNullOrWhiteSpace(r[di])).Select(r => r[di].Trim()).Distinct().Count();
            results.Add($"过滤{fcol}={filterVal}后{dcol}去重计数: {cnt}");
        }
    }

    /// <summary>
    /// 正则匹配：XX列去重计数
    /// </summary>
    private static List<string> MatchDedupPatterns(string requirements, Dictionary<string, int> colIndex, List<List<string>> allRows)
    {
        var results = new List<string>();
        var dedupPattern = new Regex(@"([^\s，,。\.\d]+)(?:列)?(?:根据|按)?\s*去重(?:计数|统计)?", RegexOptions.IgnoreCase);
        foreach (Match m in dedupPattern.Matches(requirements))
        {
            var cn = m.Groups[1].Value.Trim(); if (cn.Length < 2) continue;
            var mc = colIndex.Keys.FirstOrDefault(k => k.Contains(cn) || cn.Contains(k));
            if (mc != null && colIndex.TryGetValue(mc, out var ci))
            {
                var cnt = allRows.Where(r => r.Count > ci && !string.IsNullOrWhiteSpace(r[ci])).Select(r => r[ci].Trim()).Distinct().Count();
                if (!results.Any(r => r.StartsWith(mc + "去重计数"))) results.Add($"{mc}去重计数: {cnt}");
            }
        }

        return results;
    }

    private static void NotifyProgress(Guid reportId, string stage, int percent, string message)
        => ProgressHub.Notify(reportId, new ProgressEvent(stage, percent, message));
}

public record ColumnStat(string ColumnName, double Mean, double Median, double Min, double Max, double StdDev);
public record StatisticsResult(List<ColumnStat> ColumnStats, double MissingRate);

public static class ProgressHub
{
    private static readonly Dictionary<Guid, List<ProgressEvent>> _events = new();
    public static void Notify(Guid reportId, ProgressEvent evt) { lock (_events) { if (!_events.ContainsKey(reportId)) _events[reportId] = new(); _events[reportId].Add(evt); } }
    public static ProgressEvent? GetLatest(Guid reportId) { lock (_events) { return _events.TryGetValue(reportId, out var list) ? list.LastOrDefault() : null; } }
}
