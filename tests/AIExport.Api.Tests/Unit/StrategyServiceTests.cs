using AIExport.Api.Data;
using AIExport.Api.Models.Entities;
using AIExport.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AIExport.Api.Tests.Unit;

public class StrategyServiceTests
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

    private async Task<UploadBatch> SeedBatch(AppDbContext db, int fileCount = 2)
    {
        var user = new User { Username = "u", PasswordHash = "h", Role = UserRole.User };
        db.Users.Add(user);
        var batch = new UploadBatch { UserId = user.Id, TotalFiles = fileCount, BatchStatus = BatchStatus.AllReady };
        db.UploadBatches.Add(batch);
        await db.SaveChangesAsync();
        return batch;
    }

    private void AddFile(AppDbContext db, Guid batchId, string name, string columns)
    {
        db.UploadedFiles.Add(new UploadedFile
        {
            BatchId = batchId, OriginalName = name, StoredPath = "/tmp/" + name,
            FileSize = 100, FileFormat = FileFormat.Xlsx,
            ColumnHeaders = columns, ParseStatus = ParseStatus.Ready
        });
    }

    [Fact]
    public async Task CheckConsistency_SameColumns_ReturnsConsistent()
    {
        var db = CreateDb();
        var batch = await SeedBatch(db);
        AddFile(db, batch.Id, "f1.xlsx", "[\"姓名\",\"年龄\"]");
        AddFile(db, batch.Id, "f2.xlsx", "[\"姓名\",\"年龄\"]");
        await db.SaveChangesAsync();

        var result = await new StrategyService(db).CheckColumnConsistencyAsync(batch.Id);

        Assert.True(result.IsConsistent);
        Assert.Empty(result.Differences);
    }

    [Fact]
    public async Task CheckConsistency_DifferentColumns_ReturnsDifferences()
    {
        var db = CreateDb();
        var batch = await SeedBatch(db);
        AddFile(db, batch.Id, "f1.xlsx", "[\"姓名\",\"年龄\",\"销售额\"]");
        AddFile(db, batch.Id, "f2.xlsx", "[\"姓名\",\"年龄\",\"利润\"]");
        await db.SaveChangesAsync();

        var result = await new StrategyService(db).CheckColumnConsistencyAsync(batch.Id);

        Assert.False(result.IsConsistent);
        Assert.NotEmpty(result.Differences);
    }

    [Fact]
    public async Task CheckConsistency_IgnoresCase()
    {
        var db = CreateDb();
        var batch = await SeedBatch(db);
        AddFile(db, batch.Id, "f1.xlsx", "[\"Name\",\"Age\"]");
        AddFile(db, batch.Id, "f2.xlsx", "[\"name\",\"AGE\"]");
        await db.SaveChangesAsync();

        var result = await new StrategyService(db).CheckColumnConsistencyAsync(batch.Id);

        Assert.True(result.IsConsistent);
    }

    [Fact]
    public async Task CheckConsistency_SingleFile_SkipsCheck()
    {
        var db = CreateDb();
        var batch = await SeedBatch(db, 1);
        AddFile(db, batch.Id, "only.xlsx", "[\"A\"]");
        await db.SaveChangesAsync();

        var result = await new StrategyService(db).CheckColumnConsistencyAsync(batch.Id);

        Assert.True(result.IsConsistent);
    }

    [Fact]
    public async Task SetStrategy_UpdatesBatch()
    {
        var db = CreateDb();
        var batch = await SeedBatch(db);
        var service = new StrategyService(db);
        await service.SetStrategyAsync(batch.Id, Strategy.Merge);

        var updated = await db.UploadBatches.FindAsync(batch.Id);
        Assert.Equal(Strategy.Merge, updated!.Strategy);
    }
}
