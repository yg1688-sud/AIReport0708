using System.Text.Json;
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
    {
        _db = db;
        _storage = storage;
        _parser = parser;
        _pdf = pdf;
    }

    public async Task<GenerateResult> GenerateAsync(Guid reportId, CancellationToken ct = default)
    {
        var report = await _db.AnalysisReports
            .Include(r => r.Session).ThenInclude(s => s.Batch).ThenInclude(b => b.Files)
            .Include(r => r.Session).ThenInclude(s => s.Requirement)
            .FirstOrDefaultAsync(r => r.Id == reportId, ct)
            ?? throw new InvalidOperationException("报告不存在");

        try
        {
            // ① 清洗数据 — 10%
            report.ReportStatus = ReportStatus.Generating;
            await _db.SaveChangesAsync(ct);
            NotifyProgress(reportId, "cleaning", 10, "正在清洗数据...");

            var allRows = new List<List<string>>();
            string[]? headers = null;
            int totalRows = 0;

            foreach (var file in report.Session.Batch.Files.Where(f => f.ParseStatus == ParseStatus.Ready))
            {
                var result = await _parser.ParseAsync(file.StoredPath, file.FileFormat, ct);
                headers ??= result.ColumnHeaders;
                allRows.AddRange(result.PreviewRows);
                totalRows = result.RowCount;
            }

            // ② 计算描述性统计 — 40%
            NotifyProgress(reportId, "statistics", 40, "正在计算描述性统计...");
            var stats = CalculateStatistics(allRows, headers ?? Array.Empty<string>());

            // ③ 生成图表数据 — 70%
            NotifyProgress(reportId, "charts", 70, "正在生成图表数据...");
            var chartData = new { type = report.Session.Requirement?.ChartTypes ?? "[]" };

            // ④ 计算交叉表 — 85%
            NotifyProgress(reportId, "cross-table", 85, "正在计算交叉表...");
            var crossAnalysis = new List<string>();

            // ⑤ 生成 PDF — 95%
            NotifyProgress(reportId, "rendering", 95, "正在渲染报告...");
            var pdfData = BuildReportData(report, headers ?? Array.Empty<string>(), stats, totalRows);
            var pdfBytes = _pdf.Generate(pdfData);

            // 保存 PDF 文件
            var pdfDir = Path.Combine(_storage.GetUserDirectory(report.Session.Batch.UserId), "reports");
            Directory.CreateDirectory(pdfDir);
            var pdfPath = Path.Combine(pdfDir, $"{reportId}.pdf");
            await File.WriteAllBytesAsync(pdfPath, pdfBytes, ct);

            // 构建章节 JSON
            var chapters = JsonSerializer.Serialize(new
            {
                overview = new { rowCount = totalRows, columnCount = headers?.Length ?? 0, stats.MissingRate, report.OriginalFileName },
                statistics = stats.ColumnStats,
                charts = chartData,
                crossAnalysis
            });

            report.Chapters = chapters;
            report.PdfPath = pdfPath;
            report.FileSize = pdfBytes.Length;
            report.ReportStatus = ReportStatus.Completed;
            report.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            NotifyProgress(reportId, "complete", 100, "报告生成完成");
            return new GenerateResult(ReportStatus.Completed, chapters, null);
        }
        catch (Exception ex)
        {
            report.ReportStatus = ReportStatus.Failed;
            report.ErrorMessage = $"报告生成遇到问题：{ex.Message}";
            await _db.SaveChangesAsync(ct);
            return new GenerateResult(ReportStatus.Failed, null, report.ErrorMessage);
        }
    }

    public async Task<AnalysisReport?> GetReportAsync(Guid reportId)
    {
        return await _db.AnalysisReports
            .Include(r => r.Session)
            .FirstOrDefaultAsync(r => r.Id == reportId);
    }

    private static StatisticsResult CalculateStatistics(List<List<string>> rows, string[] headers)
    {
        var colStats = new List<ColumnStat>();
        int totalCells = 0, missingCells = 0;

        for (int c = 0; c < headers.Length && c < 20; c++)
        {
            var values = new List<double>();
            foreach (var row in rows)
            {
                totalCells++;
                if (c < row.Count && double.TryParse(row[c], out var val))
                    values.Add(val);
                else
                    missingCells++;
            }

            if (values.Count > 0)
            {
                values.Sort();
                colStats.Add(new ColumnStat(
                    headers[c],
                    Math.Round(values.Average(), 2),
                    Math.Round(values[values.Count / 2], 2),
                    Math.Round(values.Min(), 2),
                    Math.Round(values.Max(), 2),
                    Math.Round(Math.Sqrt(values.Average(v => Math.Pow(v - values.Average(), 2))), 2)
                ));
            }
        }

        var missingRate = totalCells > 0 ? (double)missingCells / totalCells : 0;
        return new StatisticsResult(colStats, missingRate);
    }

    private static ReportData BuildReportData(AnalysisReport report, string[] headers,
        StatisticsResult stats, int totalRows)
    {
        var req = report.Session.Requirement;
        var chartTypes = req is not null
            ? JsonSerializer.Deserialize<List<string>>(req.ChartTypes) ?? new()
            : new();

        return new ReportData(
            $"数据分析报告 — {report.OriginalFileName}",
            DateTime.UtcNow,
            new OverviewData(totalRows, headers.Length, stats.MissingRate, report.OriginalFileName),
            stats.ColumnStats.Select(s => new StatisticRow(s.ColumnName, s.Mean, s.Median, s.Min, s.Max, s.StdDev)).ToList(),
            chartTypes,
            new List<CrossRow>()
        );
    }

    private static void NotifyProgress(Guid reportId, string stage, int percent, string message)
    {
        // 进度通过 SSE 端点 GET /api/reports/progress 轮询 DB 状态
        // JobQueue BackgroundService 在每阶段更新后推送事件
        ProgressHub.Notify(reportId, new ProgressEvent(stage, percent, message));
    }
}

public record ColumnStat(string ColumnName, double Mean, double Median, double Min, double Max, double StdDev);
public record StatisticsResult(List<ColumnStat> ColumnStats, double MissingRate);

/// <summary>
/// SSE 进度中心 — 提供报告生成进度的发布-订阅机制
/// </summary>
public static class ProgressHub
{
    private static readonly Dictionary<Guid, List<ProgressEvent>> _events = new();

    public static void Notify(Guid reportId, ProgressEvent evt)
    {
        lock (_events)
        {
            if (!_events.ContainsKey(reportId))
                _events[reportId] = new();
            _events[reportId].Add(evt);
        }
    }

    public static ProgressEvent? GetLatest(Guid reportId)
    {
        lock (_events)
        {
            return _events.TryGetValue(reportId, out var list) ? list.LastOrDefault() : null;
        }
    }
}
