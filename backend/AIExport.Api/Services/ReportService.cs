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
    private readonly LlmClient? _llm;

    public ReportService(AppDbContext db, StorageService storage, FileParser parser, PdfGenerator pdf, LlmClient? llm = null)
    { _db = db; _storage = storage; _parser = parser; _pdf = pdf; _llm = llm; }

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

            NotifyProgress(reportId, "charts", 70, "正在生成图表数据...");
            var chartData = new { type = reqCharts, groupedData = groupedResults };

            NotifyProgress(reportId, "cross-table", 85, "正在计算交叉表...");
            var crossAnalysis = groupedResults.Select(g => { dynamic d = g; return $"{d.dimension}: 计数={d.count}, 合计={d.sum:F2}, 均值={d.avg:F2}"; }).ToList();

            // LLM 理解需求 + 代码执行计算
            var computedResults = new List<string>();
            var analysisText = "";
            var customReq = report.Session.Requirement?.CustomRequirements;

            var colIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < cols.Length; i++) colIndex[cols[i].Trim()] = i;

            // 代码直接计算（正则匹配常见分析模式：去重/过滤/比率）
            computedResults = ComputeCustomRequirements(customReq, allRows, cols);

            // LLM 补充计算（处理代码正则无法匹配的复杂场景）
            if (_llm is not null && !string.IsNullOrEmpty(customReq) && computedResults.Count == 0)
            {
                var dataSample = $"列名：{string.Join(", ", cols.Take(30))}\n总行数：{totalRows}\n";
                // 取前3行作为样本
                var sample = string.Join("\n", allRows.Take(3).Select(r => string.Join(", ", r)));
                var instrPrompt = $"用户需求：{customReq}\n\n数据文件：{report.OriginalFileName}\n{dataSample}数据样本（前3行）：\n{sample}\n\n请根据用户需求，提取计算步骤（JSON格式）：{{\"steps\":[{{\"desc\":\"步骤描述\",\"op\":\"dedup|filter|count|sum|avg|ratio\",\"column\":\"列名\",\"filterCol\":\"过滤列\",\"filterVal\":\"过滤值\",\"numerator\":\"分子描述\",\"denominator\":\"分母描述\"}}]}} 如果需求明确但无法用这些操作表达，将完整分析放在desc中。只返回JSON。";

                try
                {
                    var instrStr = await _llm.ChatForReportAsync(instrPrompt, cols);
                    // 从 LLM 回复中提取 JSON
                    var jsonStart = instrStr.IndexOf('{'); var jsonEnd = instrStr.LastIndexOf('}');
                    if (jsonStart >= 0 && jsonEnd > jsonStart)
                    {
                        var json = instrStr[jsonStart..(jsonEnd + 1)];
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("steps", out var steps))
                        {
                            foreach (var step in steps.EnumerateArray())
                            {
                                var op = step.TryGetProperty("op", out var o) ? o.GetString() ?? "" : "";
                                var col = step.TryGetProperty("column", out var c) ? c.GetString() ?? "" : "";
                                var fcol = step.TryGetProperty("filterCol", out var fc) ? fc.GetString() : null;
                                var fval = step.TryGetProperty("filterVal", out var fv) ? fv.GetString() : null;
                                var desc = step.TryGetProperty("desc", out var d) ? d.GetString() ?? "" : "";
                                var colIdx = colIndex.Keys.FirstOrDefault(k => k.Contains(col) || col.Contains(k));
                                if (colIdx is null || !colIndex.TryGetValue(colIdx, out var ci)) continue;

                                if (op == "dedup" && colIdx != null)
                                {
                                    var cnt = allRows.Where(r => r.Count > ci && !string.IsNullOrWhiteSpace(r[ci])).Select(r => r[ci].Trim()).Distinct().Count();
                                    computedResults.Add($"{desc}: {cnt}");
                                }
                                else if (op == "filter" && fcol != null && fval != null)
                                {
                                    var fci = colIndex.Keys.FirstOrDefault(k => k.Contains(fcol) || fcol.Contains(k));
                                    if (fci != null && colIndex.TryGetValue(fci, out var fi))
                                    {
                                        var filtered = allRows.Where(r => r.Count > fi && r[fi].Trim().Equals(fval, StringComparison.OrdinalIgnoreCase)).ToList();
                                        var cnt = filtered.Where(r => r.Count > ci && !string.IsNullOrWhiteSpace(r[ci])).Select(r => r[ci].Trim()).Distinct().Count();
                                        computedResults.Add($"{desc}: {cnt}");
                                    }
                                }
                                else if (op == "count") { computedResults.Add($"{desc}: {allRows.Where(r => r.Count > ci).Count()}"); }
                                else if (op == "sum" && colIdx != null) { var s = allRows.Where(r => r.Count > ci && double.TryParse(r[ci], out _)).Sum(r => double.Parse(r[ci])); computedResults.Add($"{desc}: {s:F2}"); }
                                else if (op == "avg" && colIdx != null) { var avg = allRows.Where(r => r.Count > ci && double.TryParse(r[ci], out _)).Average(r => double.Parse(r[ci])); computedResults.Add($"{desc}: {avg:F2}"); }
                                else { computedResults.Add($"{desc}: 已记录"); }
                            }
                            // 比率计算
                            var ratioSteps = steps.EnumerateArray().Where(s => s.TryGetProperty("op", out var o) && o.GetString() == "ratio");
                            foreach (var rs in ratioSteps)
                            {
                                var num = rs.TryGetProperty("numerator", out var n) ? n.GetString() ?? "" : "";
                                var den = rs.TryGetProperty("denominator", out var de) ? de.GetString() ?? "" : "";
                                var desc2 = rs.TryGetProperty("desc", out var d2) ? d2.GetString() ?? "" : "";
                                double? nv = null, dv = null;
                                foreach (var r in computedResults) { var p = r.Split(':'); if (p.Length == 2 && double.TryParse(p[1].Trim(), out var v)) { if (r.Contains(num)) nv = v; if (r.Contains(den)) dv = v; } }
                                if (nv.HasValue && dv.HasValue && dv.Value != 0) computedResults.Add($"{desc2}: {nv.Value / dv.Value * 100:F2}%");
                            }
                        }
                    }
                }
                catch { /* LLM 失败，保持 computedResults 已有值 */ }
            }

            // 格式化输出
            if (computedResults.Count > 0)
                analysisText = string.Join("\n", computedResults.Select(r => $"• {r}"));
            else if (crossAnalysis.Count > 0)
                analysisText = string.Join("\n", crossAnalysis);

            NotifyProgress(reportId, "rendering", 95, "正在渲染报告...");
            var pdfData = BuildReportData(report, cols, stats, totalRows, crossAnalysis, analysisText, computedResults);
            byte[] pdfBytes;
            try { pdfBytes = _pdf.Generate(pdfData); }
            catch (Exception ex) { Console.Error.WriteLine($"[PDF] Generate failed: {ex.Message}"); pdfBytes = Array.Empty<byte>(); }

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

    public static List<string> ComputeCustomRequirements(string? requirements, List<List<string>> allRows, string[] cols)
    {
        var results = new List<string>();
        if (string.IsNullOrEmpty(requirements) || allRows.Count == 0) return results;
        var colIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < cols.Length; i++) colIndex[cols[i].Trim()] = i;

        // 模式A: "过滤XX列=YY值，然后ZZ列去重"
        var filterPattern = new Regex(@"过滤\s*(\S+?)\s*[=＝]\s*(\S+?)\s*[，,]\s*(?:然后|再)?\s*(?:对|根据)?\s*(\S+?)\s*(?:列)?\s*去重", RegexOptions.IgnoreCase);
        foreach (Match m in filterPattern.Matches(requirements))
        {
            ComputeFilteredDedup(m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim(), m.Groups[3].Value.Trim(), colIndex, allRows, results);
        }
        // 模式B: "根据YY值过滤XX列，然后ZZ列去重"（顺序相反）
        var filterPattern2 = new Regex(@"根据\s*(\S+?)\s*过滤\s*(\S+?)\s*[，,]\s*(?:然后|再)?\s*(?:对|根据)?\s*(\S+?)\s*(?:列)?\s*去重", RegexOptions.IgnoreCase);
        foreach (Match m in filterPattern2.Matches(requirements))
        {
            ComputeFilteredDedup(m.Groups[2].Value.Trim(), m.Groups[1].Value.Trim(), m.Groups[3].Value.Trim(), colIndex, allRows, results);
        }

        // 简单去重计数（如"订单号去重"）
        var dedupResults = MatchDedupPatterns(requirements, colIndex, allRows);
        foreach (var dr in dedupResults) { if (!results.Any(r => r.StartsWith(dr.Split(':')[0]))) results.Add(dr); }

        // 比率计算：需求中含有 "/" 时，用已有数值计算比率（小值÷大值）
        if (requirements.Contains('/') || requirements.Contains('÷'))
        {
            var numericResults = results.Where(r => { var p = r.Split(':'); return p.Length >= 2 && double.TryParse(p[1].Trim(), out _); }).ToList();
            if (numericResults.Count >= 2)
            {
                var vals = numericResults.Select(r => double.Parse(r.Split(':')[1].Trim())).OrderBy(v => v).ToList();
                var ratio = vals.First() / vals.Last() * 100;
                results.Add($"比率: {ratio:F2}%");
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
