using System.Text.Json;
using AIExport.Api.Infrastructure;
using AIExport.Api.Services;

namespace AIExport.Api.Endpoints;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api").RequireAuthorization();

        // 提交报告生成任务（在 ChatEndpoints.confirm 中自动触发，此端点供手动重试使用）
        group.MapPost("/reports/{reportId}/generate", async (Guid reportId, ReportService reportService, JobQueue queue) =>
        {
            await queue.EnqueueAsync(new ReportJob(reportId, reportId, reportId));
            return Results.Ok(new { taskId = reportId, message = "报告生成任务已提交" });
        });

        // SSE 报告生成进度
        group.MapGet("/reports/progress", async (Guid taskId, HttpContext context, CancellationToken ct) =>
        {
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers["Cache-Control"] = "no-cache";

            while (!ct.IsCancellationRequested)
            {
                var evt = ProgressHub.GetLatest(taskId);
                if (evt is not null)
                {
                    var json = JsonSerializer.Serialize(evt);
                    await context.Response.WriteAsync($"event: progress\ndata: {json}\n\n", ct);
                    await context.Response.Body.FlushAsync(ct);

                    if (evt.Stage == "complete") break;
                }

                await Task.Delay(500, ct);
            }
        }).AllowAnonymous();

        // 获取报告详情
        group.MapGet("/reports/{reportId}", async (Guid reportId, ReportService reportService) =>
        {
            var report = await reportService.GetReportAsync(reportId);
            if (report is null)
                return Results.NotFound(new { error = new { code = "NOT_FOUND", message = "报告不存在" } });

            return Results.Ok(new
            {
                report.Id,
                report.ReportStatus,
                report.Mode,
                report.ReportType,
                report.Chapters,
                report.CreatedAt,
                report.CompletedAt
            });
        });

        // 下载 PDF
        group.MapGet("/reports/{reportId}/download", async (Guid reportId, ReportService reportService) =>
        {
            var report = await reportService.GetReportAsync(reportId);
            if (report?.PdfPath is null || !File.Exists(report.PdfPath))
                return Results.NotFound(new { error = new { code = "NOT_FOUND", message = "报告文件不存在" } });

            var stream = File.OpenRead(report.PdfPath);
            return Results.File(stream, "application/pdf", $"report-{reportId.ToString("N")[..8]}.pdf",
                enableRangeProcessing: true);
        });
    }
}
