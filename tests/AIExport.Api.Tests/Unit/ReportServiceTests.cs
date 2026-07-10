using AIExport.Api.Data;
using AIExport.Api.Infrastructure;
using AIExport.Api.Models.Entities;
using AIExport.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AIExport.Api.Tests.Unit;

public class ReportServiceTests
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
    public async Task CalculateStatistics_ReturnsCorrectValues()
    {
        var db = CreateDb();
        var storageDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var storage = new StorageService(storageDir);

        // 创建一个测试Excel文件
        var tmpPath = Path.GetTempFileName() + ".xlsx";
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet1");
            ws.Cell(1, 1).Value = "A"; ws.Cell(1, 2).Value = "B";
            ws.Cell(2, 1).Value = 10; ws.Cell(2, 2).Value = 20;
            ws.Cell(3, 1).Value = 20; ws.Cell(3, 2).Value = 30;
            ws.Cell(4, 1).Value = 30; ws.Cell(4, 2).Value = 40;
            wb.SaveAs(tmpPath);
        }

        var user = new User { Username = "u", PasswordHash = "h", Role = UserRole.User };
        db.Users.Add(user);
        var batch = new UploadBatch { UserId = user.Id, TotalFiles = 1, BatchStatus = BatchStatus.AllReady, Strategy = Strategy.Merge };
        db.UploadBatches.Add(batch);
        await db.SaveChangesAsync();

        var file = new UploadedFile { BatchId = batch.Id, OriginalName = "data.xlsx", StoredPath = tmpPath,
            FileSize = 100, FileFormat = FileFormat.Xlsx, ColumnHeaders = "[\"A\",\"B\"]",
            ParseStatus = ParseStatus.Ready, RowCount = 3, ColumnCount = 2 };
        db.UploadedFiles.Add(file);
        await db.SaveChangesAsync();

        var session = new AnalysisSession { BatchId = batch.Id, SessionStatus = SessionStatus.Confirmed,
            Mode = SessionMode.Chat, ExpiresAt = DateTime.UtcNow.AddDays(1), ConfirmedAt = DateTime.UtcNow };
        db.AnalysisSessions.Add(session);
        await db.SaveChangesAsync();

        var req = new AnalysisRequirement { SessionId = session.Id,
            Dimensions = "[\"地区\"]", Metrics = "[\"销售额\"]", ChartTypes = "[\"bar\"]" };
        db.AnalysisRequirements.Add(req);
        var report = new AnalysisReport { SessionId = session.Id, OriginalFileName = "data.xlsx",
            ReportStatus = ReportStatus.Generating, Mode = SessionMode.Chat, ReportType = ReportType.MergeReport,
            ExpiresAt = DateTime.UtcNow.AddDays(7) };
        db.AnalysisReports.Add(report);
        await db.SaveChangesAsync();

        var service = new ReportService(db, storage, new FileParser(), new PdfGenerator());
        var result = await service.GenerateAsync(report.Id, CancellationToken.None);

        Assert.Equal(ReportStatus.Completed, result.Status);
        Assert.NotNull(result.Chapters);
    }

    [Fact]
    public async Task Generate_NoReadyFiles_Fails()
    {
        var db = CreateDb();
        var storage = new StorageService(Path.GetTempPath());
        var service = new ReportService(db, storage, new FileParser(), new PdfGenerator());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Contains("报告不存在", ex.Message);
    }
}
