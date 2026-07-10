using System.Security.Claims;
using System.Text.Json;
using AIExport.Api.Data;
using AIExport.Api.Models.Dtos;
using AIExport.Api.Services;

namespace AIExport.Api.Endpoints;

public static class FileEndpoints
{
    public static void MapFileEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api").RequireAuthorization();

        // 上传文件
        group.MapPost("/files/upload", async (HttpRequest request, FileService fileService, ClaimsPrincipal user) =>
        {
            var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var form = await request.ReadFormAsync();
            var files = form.Files.GetFiles("files");

            if (files is null || files.Count == 0)
                return Results.BadRequest(new { error = new { code = "NO_FILES", message = "请选择要上传的文件" } });

            if (files.Count > 20)
                return Results.BadRequest(new { error = new { code = "TOO_MANY", message = "单次最多上传 20 个文件" } });

            // 创建批次
            var batch = await fileService.CreateBatchAsync(userId);

            var results = new List<FileInfoDto>();
            foreach (var formFile in files)
            {
                using var stream = formFile.OpenReadStream();
                var ext = Path.GetExtension(formFile.FileName);
                var format = ext.ToLower() switch
                {
                    ".xlsx" => "xlsx",
                    ".xls" => "xls",
                    ".csv" => "csv",
                    _ => null
                };

                if (format is null)
                {
                    results.Add(new FileInfoDto(Guid.Empty, formFile.FileName, formFile.Length, ext, "failed", null, null));
                    continue;
                }

                try
                {
                    var uploadedFile = await fileService.AddFileAsync(batch.Id, userId, formFile.FileName, stream);

                    // 异步解析（fire-and-forget，通过 SSE 推送状态）
                    _ = Task.Run(async () =>
                    {
                        await fileService.ParseFileAsync(uploadedFile.Id);
                        await ProgressNotifier.NotifyFileProgress(uploadedFile.Id, batch.Id);
                    });

                    results.Add(new FileInfoDto(uploadedFile.Id, uploadedFile.OriginalName,
                        uploadedFile.FileSize, format, "parsing", null, null));
                }
                catch (Exception ex)
                {
                    results.Add(new FileInfoDto(Guid.Empty, formFile.FileName, formFile.Length,
                        format ?? "unknown", "failed", null, null));
                }
            }

            return Results.Ok(new UploadResponse(batch.Id, results));
        }).DisableAntiforgery();

        // SSE 文件解析进度
        group.MapGet("/files/progress", async (Guid batchId, HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers["Cache-Control"] = "no-cache";
            context.Response.Headers["X-Accel-Buffering"] = "no";

            while (!ct.IsCancellationRequested)
            {
                var batch = await db.UploadBatches.FindAsync(new object[] { batchId }, ct);
                if (batch is null) break;

                var eventData = JsonSerializer.Serialize(new
                {
                    batchId = batch.Id,
                    readyFiles = batch.ReadyFiles,
                    totalFiles = batch.TotalFiles,
                    batchStatus = batch.BatchStatus.ToString()
                });

                await context.Response.WriteAsync($"event: batch-progress\ndata: {eventData}\n\n", ct);
                await context.Response.Body.FlushAsync(ct);

                if (batch.BatchStatus == Models.Entities.BatchStatus.AllReady)
                {
                    await context.Response.WriteAsync($"event: batch-ready\ndata: {eventData}\n\n", ct);
                    break;
                }

                await Task.Delay(1000, ct);
            }
        }).AllowAnonymous(); // SSE 不验证（简化）— 可以添加 token 参数

        // === 多文件策略选择 (US3) ===

        // 设置策略
        group.MapPost("/batches/{batchId}/strategy", async (Guid batchId, SetStrategyRequest request,
            StrategyService strategyService, ClaimsPrincipal user) =>
        {
            var strategy = request.Strategy.ToLower() == "merge" ? Models.Entities.Strategy.Merge : Models.Entities.Strategy.Separate;
            await strategyService.SetStrategyAsync(batchId, strategy);

            if (strategy == Models.Entities.Strategy.Merge)
            {
                var consistency = await strategyService.CheckColumnConsistencyAsync(batchId);
                return Results.Ok(new
                {
                    strategy = request.Strategy,
                    isConsistent = consistency.IsConsistent,
                    differences = consistency.Differences.Select(d => new
                    {
                        fileName = d.FileName,
                        missing = d.Missing,
                        extra = d.Extra
                    })
                });
            }

            return Results.Ok(new { strategy = request.Strategy, isConsistent = true, differences = Array.Empty<object>() });
        });

        // 检查列一致性
        group.MapGet("/batches/{batchId}/consistency", async (Guid batchId, StrategyService strategyService) =>
        {
            var result = await strategyService.CheckColumnConsistencyAsync(batchId);
            return Results.Ok(new
            {
                isConsistent = result.IsConsistent,
                differences = result.Differences.Select(d => new
                {
                    fileName = d.FileName,
                    missing = d.Missing,
                    extra = d.Extra
                })
            });
        });
    }
}

public record SetStrategyRequest(string Strategy);

/// <summary>
/// SSE 进度通知辅助类 — 各 Service 解析完成后调用，通知等待的 SSE 连接
/// </summary>
public static class ProgressNotifier
{
    public static Task NotifyFileProgress(Guid fileId, Guid batchId)
    {
        // SSE 轮询模式：GET /api/files/progress 轮询数据库状态
        // 无需主动推送 — SSE 端点定期查询 DB 即可
        return Task.CompletedTask;
    }
}
