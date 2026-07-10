using System.IO.Compression;
using System.Security.Claims;
using AIExport.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AIExport.Api.Endpoints;

public static class HistoryEndpoints
{
    public static void MapHistoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api").RequireAuthorization();

        // 历史报告列表（分页+搜索+筛选）
        group.MapGet("/reports", async (int page, int pageSize, string? keyword,
            DateTime? dateFrom, DateTime? dateTo, AppDbContext db, ClaimsPrincipal user) =>
        {
            var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = db.AnalysisReports.AsQueryable();

            if (!string.IsNullOrWhiteSpace(keyword))
                query = query.Where(r => r.OriginalFileName.Contains(keyword));
            if (dateFrom.HasValue)
                query = query.Where(r => r.CreatedAt >= dateFrom.Value);
            if (dateTo.HasValue)
                query = query.Where(r => r.CreatedAt <= dateTo.Value);

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(r => new
                {
                    r.Id, r.OriginalFileName, ReportStatus = r.ReportStatus.ToString().ToLower(),
                    Mode = r.Mode.ToString().ToLower(), ReportType = r.ReportType.ToString().ToLower(),
                    r.FileSize, r.CreatedAt, r.ExpiresAt
                })
                .ToListAsync();

            return Results.Ok(new { items, total, page, pageSize });
        });

        // 删除报告
        group.MapDelete("/reports/{reportId}", async (Guid reportId, AppDbContext db) =>
        {
            var report = await db.AnalysisReports.FindAsync(reportId);
            if (report is null)
                return Results.NotFound(new { error = new { code = "NOT_FOUND", message = "报告不存在" } });

            if (report.PdfPath is not null && File.Exists(report.PdfPath))
                File.Delete(report.PdfPath);

            db.AnalysisReports.Remove(report);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // 批量下载（打包 ZIP）
        group.MapPost("/reports/batch-download", async (BatchDownloadRequest request, AppDbContext db) =>
        {
            var reports = await db.AnalysisReports
                .Where(r => request.ReportIds.Contains(r.Id))
                .ToListAsync();

            var zipStream = new MemoryStream();
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
            {
                foreach (var report in reports)
                {
                    if (report.PdfPath is null || !File.Exists(report.PdfPath)) continue;
                    var entry = archive.CreateEntry($"{report.OriginalFileName}-{report.CreatedAt:yyyyMMdd}.pdf");
                    await using var entryStream = entry.Open();
                    await using var fileStream = File.OpenRead(report.PdfPath);
                    await fileStream.CopyToAsync(entryStream);
                }
            }
            zipStream.Position = 0;
            return Results.File(zipStream, "application/zip", "reports.zip");
        });
    }
}

public record BatchDownloadRequest(List<Guid> ReportIds);
