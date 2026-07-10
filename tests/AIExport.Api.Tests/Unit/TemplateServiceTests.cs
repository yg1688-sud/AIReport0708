using AIExport.Api.Data;
using AIExport.Api.Models.Entities;
using AIExport.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AIExport.Api.Tests.Unit;

public class TemplateServiceTests
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

    private async Task<User> SeedUser(AppDbContext db)
    {
        var user = new User { Username = "u", PasswordHash = "h", Role = UserRole.User };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task SaveTemplate_CreatesTemplate()
    {
        var db = CreateDb();
        var user = await SeedUser(db);

        var batch = new UploadBatch { UserId = user.Id, TotalFiles = 1, BatchStatus = BatchStatus.AllReady };
        db.UploadBatches.Add(batch);
        await db.SaveChangesAsync();

        var file = new UploadedFile { BatchId = batch.Id, OriginalName = "data.xlsx",
            StoredPath = "/tmp/x", FileSize = 100, FileFormat = FileFormat.Xlsx,
            ColumnHeaders = "[\"地区\",\"销售额\"]", ParseStatus = ParseStatus.Ready };
        db.UploadedFiles.Add(file);
        await db.SaveChangesAsync();

        var session = new AnalysisSession
        {
            BatchId = batch.Id, SessionStatus = SessionStatus.Confirmed,
            Mode = SessionMode.Chat, ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };
        db.AnalysisSessions.Add(session);
        await db.SaveChangesAsync();

        var requirement = new AnalysisRequirement
        {
            SessionId = session.Id, Dimensions = "[\"地区\"]", Metrics = "[\"销售额\"]",
            ChartTypes = "[\"bar\"]"
        };
        db.AnalysisRequirements.Add(requirement);
        await db.SaveChangesAsync();

        var service = new TemplateService(db);
        var template = await service.SaveTemplateAsync(session.Id, user.Id, "月度分析");

        Assert.NotNull(template);
        Assert.Equal("月度分析", template.Name);
        Assert.Equal(Strategy.Merge, template.Strategy);
    }

    [Fact]
    public async Task GetUserTemplates_ReturnsOnlyUserTemplates()
    {
        var db = CreateDb();
        var u1 = await SeedUser(db);
        var u2 = new User { Username = "u2", PasswordHash = "h", Role = UserRole.User };
        db.Users.Add(u2);

        db.AnalysisTemplates.Add(new AnalysisTemplate
            { UserId = u1.Id, Name = "T1", Dimensions = "[]", Metrics = "[]", ChartTypes = "[]", ColumnNames = "[]", Strategy = Strategy.Merge });
        db.AnalysisTemplates.Add(new AnalysisTemplate
            { UserId = u2.Id, Name = "T2", Dimensions = "[]", Metrics = "[]", ChartTypes = "[]", ColumnNames = "[]", Strategy = Strategy.Separate });
        await db.SaveChangesAsync();

        var service = new TemplateService(db);
        var templates = await service.GetUserTemplatesAsync(u1.Id);

        Assert.Single(templates);
        Assert.Equal("T1", templates[0].Name);
    }

    [Fact]
    public async Task DeleteTemplate_RemovesTemplate()
    {
        var db = CreateDb();
        var user = await SeedUser(db);
        var t = new AnalysisTemplate { UserId = user.Id, Name = "T", Dimensions = "[]",
            Metrics = "[]", ChartTypes = "[]", ColumnNames = "[]", Strategy = Strategy.Merge };
        db.AnalysisTemplates.Add(t);
        await db.SaveChangesAsync();

        var service = new TemplateService(db);
        await service.DeleteTemplateAsync(t.Id, user.Id);

        Assert.Empty(db.AnalysisTemplates);
    }

    [Fact]
    public async Task SaveTemplate_DuplicateName_Throws()
    {
        var db = CreateDb();
        var user = await SeedUser(db);
        db.AnalysisTemplates.Add(new AnalysisTemplate { UserId = user.Id, Name = "Dup",
            Dimensions = "[]", Metrics = "[]", ChartTypes = "[]", ColumnNames = "[]", Strategy = Strategy.Merge });
        await db.SaveChangesAsync();

        var batch = new UploadBatch { UserId = user.Id, TotalFiles = 1, BatchStatus = BatchStatus.AllReady };
        db.UploadBatches.Add(batch);
        await db.SaveChangesAsync();

        var file = new UploadedFile { BatchId = batch.Id, OriginalName = "d.xlsx",
            StoredPath = "/tmp/d", FileSize = 100, FileFormat = FileFormat.Xlsx,
            ColumnHeaders = "[\"A\"]", ParseStatus = ParseStatus.Ready };
        db.UploadedFiles.Add(file);
        await db.SaveChangesAsync();

        var session = new AnalysisSession { BatchId = batch.Id, SessionStatus = SessionStatus.Confirmed,
            Mode = SessionMode.Chat, ExpiresAt = DateTime.UtcNow.AddMinutes(30) };
        db.AnalysisSessions.Add(session);
        await db.SaveChangesAsync();

        var req = new AnalysisRequirement { SessionId = session.Id, Dimensions = "[]", Metrics = "[]", ChartTypes = "[]" };
        db.AnalysisRequirements.Add(req);
        await db.SaveChangesAsync();

        var service = new TemplateService(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveTemplateAsync(session.Id, user.Id, "Dup"));
    }

    [Fact]
    public async Task ValidateColumns_MissingColumn_ReturnsInvalid()
    {
        var db = CreateDb();
        var user = await SeedUser(db);
        var batch = new UploadBatch { UserId = user.Id, TotalFiles = 1, BatchStatus = BatchStatus.AllReady };
        db.UploadBatches.Add(batch);
        db.UploadedFiles.Add(new UploadedFile
        {
            BatchId = batch.Id, OriginalName = "f.xlsx", StoredPath = "/tmp/f",
            FileSize = 100, FileFormat = FileFormat.Xlsx,
            ColumnHeaders = "[\"A\",\"B\"]", ParseStatus = ParseStatus.Ready
        });
        var t = new AnalysisTemplate { UserId = user.Id, Name = "T", Dimensions = "[]",
            Metrics = "[]", ChartTypes = "[]", ColumnNames = "[\"A\",\"B\",\"C\"]", Strategy = Strategy.Merge };
        db.AnalysisTemplates.Add(t);
        await db.SaveChangesAsync();

        var service = new TemplateService(db);
        var result = await service.ValidateTemplateColumnsAsync(t.Id, batch.Id);

        Assert.False(result.Valid);
        Assert.Contains("C", result.MissingColumns);
    }
}
