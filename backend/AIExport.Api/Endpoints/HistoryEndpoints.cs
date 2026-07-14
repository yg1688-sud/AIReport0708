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
        group.MapGet("/reports", async (int? page, int? pageSize, string? keyword,
            string? dateFrom, string? dateTo, AppDbContext db, ClaimsPrincipal user) =>
        {
            var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var p = Math.Max(1, page ?? 1);
            var ps = Math.Clamp(pageSize ?? 20, 1, 100);
            DateTime? df = string.IsNullOrWhiteSpace(dateFrom) ? (DateTime?)null : DateTime.Parse(dateFrom);
            DateTime? dt = string.IsNullOrWhiteSpace(dateTo) ? (DateTime?)null : DateTime.Parse(dateTo);

            var query = db.AnalysisReports
                .Include(r => r.Session).ThenInclude(s => s.Batch)
                .Where(r => r.Session.Batch.UserId == userId)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(keyword))
                query = query.Where(r => r.OriginalFileName.Contains(keyword));
            if (df.HasValue)
                query = query.Where(r => r.CreatedAt >= df.Value);
            if (dt.HasValue)
                query = query.Where(r => r.CreatedAt <= dt.Value);

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(r => r.CreatedAt)
                .Skip((p - 1) * ps)
                .Take(ps)
                .Select(r => new
                {
                    r.Id, r.OriginalFileName, ReportStatus = r.ReportStatus.ToString().ToLower(),
                    Mode = r.Mode.ToString().ToLower(), ReportType = r.ReportType.ToString().ToLower(),
                    r.FileSize, r.CreatedAt, r.ExpiresAt
                })
                .ToListAsync();

            return Results.Ok(new { items, total, page = p, pageSize = ps });
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
