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

        // 上传文件（支持追加到已有批次）
        group.MapPost("/files/upload", async (HttpRequest request, FileService fileService, ClaimsPrincipal user) =>
        {
            var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var form = await request.ReadFormAsync();
            var files = form.Files.GetFiles("files");
            var batchIdStr = form["batchId"].FirstOrDefault();

            if (files is null || files.Count == 0)
                return Results.BadRequest(new { error = new { code = "NO_FILES", message = "请选择要上传的文件" } });

            // 追加到已有批次或创建新批次
            Guid batchId;
            if (!string.IsNullOrEmpty(batchIdStr) && Guid.TryParse(batchIdStr, out var existingId))
            {
                batchId = existingId;
                // 检查批次是否属于当前用户
                var existingBatch = await fileService.GetBatchAsync(batchId);
                if (existingBatch is null || existingBatch.UserId != userId)
                    return Results.BadRequest(new { error = new { code = "INVALID", message = "批次不存在" } });
                if (existingBatch.TotalFiles + files.Count > 20)
                    return Results.BadRequest(new { error = new { code = "TOO_MANY", message = "单次最多上传 20 个文件" } });
            }
            else
            {
                batchId = (await fileService.CreateBatchAsync(userId)).Id;
            }
            var results = new List<FileInfoDto>();

            foreach (var formFile in files)
            {
                using var stream = formFile.OpenReadStream();
                var ext = Path.GetExtension(formFile.FileName);
                var format = ext.ToLower() switch
                {
                    ".xlsx" => "xlsx", ".xls" => "xls", ".csv" => "csv", _ => null
                };

                if (format is null)
                {
                    results.Add(new FileInfoDto(Guid.Empty, formFile.FileName, formFile.Length, ext, "failed", null, null));
                    continue;
                }

                try
                {
                    var uploadedFile = await fileService.AddFileAsync(batchId, userId, formFile.FileName, stream);

                    var scopeFactory = app.Services.GetRequiredService<IServiceScopeFactory>();
                    var fileId = uploadedFile.Id;
                    _ = Task.Run(async () =>
                    {
                        using var scope = scopeFactory.CreateScope();
                        var svc = scope.ServiceProvider.GetRequiredService<FileService>();
                        await svc.ParseFileAsync(fileId);
                    });

                    results.Add(new FileInfoDto(uploadedFile.Id, uploadedFile.OriginalName,
                        uploadedFile.FileSize, format, "parsing", null, null));
                }
                catch (Exception)
                {
                    results.Add(new FileInfoDto(Guid.Empty, formFile.FileName, formFile.Length,
                        format ?? "unknown", "failed", null, null));
                }
            }

            return Results.Ok(new UploadResponse(batchId, results));
        }).DisableAntiforgery();

        // 删除单个文件
        group.MapDelete("/files/{fileId}", async (Guid fileId, FileService fileService) =>
        {
            await fileService.DeleteFileAsync(fileId);
            return Results.NoContent();
        });

        // 清空批次
        group.MapDelete("/batches/{batchId}/files", async (Guid batchId, FileService fileService) =>
        {
            await fileService.ClearBatchAsync(batchId);
            return Results.NoContent();
        });

        // === 多文件策略选择 ===
        group.MapPost("/batches/{batchId}/strategy", async (Guid batchId, SetStrategyRequest request,
            StrategyService strategyService) =>
        {
            var strategy = request.Strategy.ToLower() == "merge"
                ? Models.Entities.Strategy.Merge : Models.Entities.Strategy.Separate;
            await strategyService.SetStrategyAsync(batchId, strategy);

            if (strategy == Models.Entities.Strategy.Merge)
            {
                var consistency = await strategyService.CheckColumnConsistencyAsync(batchId);
                return Results.Ok(new
                {
                    strategy = request.Strategy, isConsistent = consistency.IsConsistent,
                    differences = consistency.Differences.Select(d => new
                    { fileName = d.FileName, missing = d.Missing, extra = d.Extra })
                });
            }

            return Results.Ok(new { strategy = request.Strategy, isConsistent = true, differences = Array.Empty<object>() });
        });

        group.MapGet("/batches/{batchId}/consistency", async (Guid batchId, StrategyService strategyService) =>
        {
            var result = await strategyService.CheckColumnConsistencyAsync(batchId);
            return Results.Ok(new
            {
                isConsistent = result.IsConsistent,
                differences = result.Differences.Select(d => new
                { fileName = d.FileName, missing = d.Missing, extra = d.Extra })
            });
        });
    }

    // 文件进度轮询（替代 SSE，避免 EventSource CORS 问题）
    public static void MapFileSseEndpoint(this WebApplication app)
    {
        app.MapGet("/api/files/progress/poll", async (Guid batchId, AppDbContext db) =>
        {
            var batch = await db.UploadBatches.FindAsync(batchId);
            if (batch is null) return Results.NotFound();
            return Results.Ok(new {
                batchId = batch.Id, readyFiles = batch.ReadyFiles,
                totalFiles = batch.TotalFiles, batchStatus = batch.BatchStatus.ToString()
            });
        });

        app.MapGet("/api/files/progress", async (Guid batchId, HttpContext context, AppDbContext db, CancellationToken ct) =>
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
                    batchId = batch.Id, readyFiles = batch.ReadyFiles,
                    totalFiles = batch.TotalFiles, batchStatus = batch.BatchStatus.ToString()
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
        });
    }
}

public record SetStrategyRequest(string Strategy);

public static class ProgressNotifier
{
    public static Task NotifyFileProgress(Guid fileId, Guid batchId) => Task.CompletedTask;
}
