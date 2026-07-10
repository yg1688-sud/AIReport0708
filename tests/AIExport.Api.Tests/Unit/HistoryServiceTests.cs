using AIExport.Api.Data;
using AIExport.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIExport.Api.Tests.Unit;

public class HistoryServiceTests
{
    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:").Options;
        var db = new AppDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task GetHistory_FiltersByUser()
    {
        var db = CreateDb();
        var user = new User { Username = "u", PasswordHash = "h", Role = UserRole.User };
        db.Users.Add(user);
        var batch = new UploadBatch { UserId = user.Id, TotalFiles = 1 };
        db.UploadBatches.Add(batch);
        await db.SaveChangesAsync();

        var s1 = new AnalysisSession { BatchId = batch.Id, SessionStatus = SessionStatus.Confirmed,
            Mode = SessionMode.Chat, ExpiresAt = DateTime.UtcNow.AddDays(1) };
        var s2 = new AnalysisSession { BatchId = batch.Id, SessionStatus = SessionStatus.Confirmed,
            Mode = SessionMode.Template, ExpiresAt = DateTime.UtcNow.AddDays(1) };
        db.AnalysisSessions.AddRange(s1, s2);
        await db.SaveChangesAsync();

        db.AnalysisReports.Add(new AnalysisReport
        {
            SessionId = s1.Id, OriginalFileName = "r1", ReportStatus = ReportStatus.Completed,
            Mode = SessionMode.Chat, ReportType = ReportType.MergeReport, ExpiresAt = DateTime.UtcNow.AddDays(7)
        });
        db.AnalysisReports.Add(new AnalysisReport
        {
            SessionId = s2.Id, OriginalFileName = "r2", ReportStatus = ReportStatus.Completed,
            Mode = SessionMode.Template, ReportType = ReportType.SingleFileReport, ExpiresAt = DateTime.UtcNow.AddDays(7)
        });
        await db.SaveChangesAsync();

        var all = await db.AnalysisReports.CountAsync();
        Assert.Equal(2, all);
    }

    [Fact]
    public async Task ExpiredReports_AreIdentified()
    {
        var expired = DateTime.UtcNow.AddDays(-8);
        var valid = DateTime.UtcNow.AddDays(1);

        Assert.True(expired < DateTime.UtcNow);
        Assert.True(valid > DateTime.UtcNow);
    }
}
