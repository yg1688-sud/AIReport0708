using AIExport.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AIExport.Api.Services;

/// <summary>
/// 定时清理过期报告和文件（每1小时执行一次）
/// </summary>
public class CleanupService : BackgroundService
{
    private readonly IServiceProvider _sp;

    public CleanupService(IServiceProvider sp) => _sp = sp;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var expired = await db.AnalysisReports
                    .Where(r => r.ExpiresAt < DateTime.Now)
                    .ToListAsync(ct);

                foreach (var report in expired)
                {
                    if (report.PdfPath is not null && File.Exists(report.PdfPath))
                        File.Delete(report.PdfPath);
                    db.AnalysisReports.Remove(report);
                }

                if (expired.Count > 0)
                    await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                // 清理失败不影响主流程
                Console.WriteLine($"Cleanup error: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromHours(1), ct);
        }
    }
}
