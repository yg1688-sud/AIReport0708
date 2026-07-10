using AIExport.Api.Data;
using AIExport.Api.Models.Entities;
using AIExport.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AIExport.Api.Tests.Unit;

public class ChatServiceTests
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

    private async Task<(AppDbContext, UploadBatch, User)> SeedAsync()
    {
        var db = CreateDb();
        var user = new User { Username = "u", PasswordHash = "h", Role = UserRole.User };
        db.Users.Add(user);
        var batch = new UploadBatch { UserId = user.Id, TotalFiles = 1, BatchStatus = BatchStatus.AllReady, Strategy = Strategy.Merge };
        db.UploadBatches.Add(batch);
        await db.SaveChangesAsync();
        var file = new UploadedFile { BatchId = batch.Id, OriginalName = "f.xlsx", StoredPath = "/tmp/f",
            FileSize = 100, FileFormat = FileFormat.Xlsx, ColumnHeaders = "[\"日期\",\"销售额\",\"地区\"]", ParseStatus = ParseStatus.Ready };
        db.UploadedFiles.Add(file);
        await db.SaveChangesAsync();
        return (db, batch, user);
    }

    [Fact]
    public async Task StartSession_ChatMode_ReturnsFirstMessage()
    {
        var (db, batch, _) = await SeedAsync();
        var service = new ChatService(db, null!); // LLM client not needed for session start

        var (session, firstMsg) = await service.StartSessionAsync(batch.Id, batch.Strategy!.Value, null);

        Assert.NotNull(session);
        Assert.Equal(SessionMode.Chat, session.Mode);
        Assert.Contains("数据", firstMsg);
        Assert.Single(db.ChatMessages);
    }

    [Fact]
    public async Task StartSession_TemplateMode_SetsMode()
    {
        var (db, batch, user) = await SeedAsync();
        var template = new AnalysisTemplate { UserId = user.Id, Name = "T", Dimensions = "[]",
            Metrics = "[]", ChartTypes = "[]", ColumnNames = "[\"日期\"]", Strategy = Strategy.Merge };
        db.AnalysisTemplates.Add(template);
        await db.SaveChangesAsync();

        var service = new ChatService(db, null!);
        var (session, _) = await service.StartSessionAsync(batch.Id, batch.Strategy!.Value, template.Id);

        Assert.Equal(SessionMode.Template, session.Mode);
    }

    [Fact]
    public async Task SendMessage_AddsMessages()
    {
        var (db, batch, _) = await SeedAsync();
        var service = new ChatService(db, null!);
        var (session, _) = await service.StartSessionAsync(batch.Id, batch.Strategy!.Value, null);

        // 规则引擎模式（无 LLM）应返回追问
        var reply = await service.SendMessageAsync(session.Id, "帮我分析销售额");

        Assert.NotNull(reply);
        Assert.Equal(3, db.ChatMessages.Count()); // 引导消息 + 用户消息 + 系统回复
    }

    [Fact]
    public async Task ConfirmRequirements_CreatesRequirement()
    {
        var (db, batch, _) = await SeedAsync();
        var service = new ChatService(db, null!);
        var (session, _) = await service.StartSessionAsync(batch.Id, batch.Strategy!.Value, null);
        await service.SendMessageAsync(session.Id, "分析销售额趋势");
        await service.SendMessageAsync(session.Id, "确认");

        var (success, taskId) = await service.ConfirmRequirementsAsync(session.Id);

        Assert.True(success);
        Assert.NotEqual(Guid.Empty, taskId);

        var updated = await db.AnalysisSessions.Include(s => s.Requirement).FirstAsync(s => s.Id == session.Id);
        Assert.Equal(SessionStatus.Confirmed, updated.SessionStatus);
        Assert.NotNull(updated.Requirement);
    }

    [Fact]
    public async Task Confirm_WithoutConversation_Fails()
    {
        var (db, batch, _) = await SeedAsync();
        var service = new ChatService(db, null!);
        var (session, _) = await service.StartSessionAsync(batch.Id, batch.Strategy!.Value, null);

        var (success, _) = await service.ConfirmRequirementsAsync(session.Id);

        Assert.False(success);
    }

    [Fact]
    public async Task SendMessage_NonexistentColumn_ReturnsGuidance()
    {
        var (db, batch, _) = await SeedAsync();
        var service = new ChatService(db, null!);
        var (session, _) = await service.StartSessionAsync(batch.Id, batch.Strategy!.Value, null);

        // 规则引擎模式：即使提到不存在的列，也应返回引导消息
        var reply = await service.SendMessageAsync(session.Id, "我想分析利润");

        Assert.NotNull(reply);
        Assert.True(reply.Length > 10); // 有实质性回复
    }
}
