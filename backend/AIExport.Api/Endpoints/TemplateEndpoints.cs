using System.Security.Claims;
using AIExport.Api.Models.Dtos;
using AIExport.Api.Services;

namespace AIExport.Api.Endpoints;

public static class TemplateEndpoints
{
    public static void MapTemplateEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api").RequireAuthorization();

        // 获取模版列表
        group.MapGet("/templates", async (TemplateService service, ClaimsPrincipal user) =>
        {
            var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var templates = await service.GetUserTemplatesAsync(userId);
            return Results.Ok(new
            {
                templates = templates.Select(t => new TemplateDto(
                    t.Id, t.Name,
                    t.Strategy.ToString().ToLower(),
                    System.Text.Json.JsonSerializer.Deserialize<List<string>>(t.ColumnNames) ?? new(),
                    t.CreatedAt
                ))
            });
        });

        // 保存模版
        group.MapPost("/templates", async (SaveTemplateRequest request, TemplateService service,
            ClaimsPrincipal user) =>
        {
            var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            try
            {
                var template = await service.SaveTemplateAsync(request.SessionId, userId, request.Name);
                return Results.Created($"/api/templates/{template.Id}", new
                {
                    template.Id, template.Name,
                    message = "模版保存成功"
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("已存在"))
            {
                return Results.Conflict(new { error = new { code = "DUPLICATE", message = ex.Message } });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = new { code = "INVALID", message = ex.Message } });
            }
        });

        // 覆盖模版
        group.MapPut("/templates/{templateId}", async (Guid templateId, SaveTemplateRequest request,
            TemplateService service, ClaimsPrincipal user) =>
        {
            var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await service.DeleteTemplateAsync(templateId, userId);
            var template = await service.SaveTemplateAsync(request.SessionId, userId, request.Name);
            return Results.Ok(new { message = "模版已覆盖保存", template.Id, template.Name });
        });

        // 删除模版
        group.MapDelete("/templates/{templateId}", async (Guid templateId, TemplateService service,
            ClaimsPrincipal user) =>
        {
            var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            try
            {
                await service.DeleteTemplateAsync(templateId, userId);
                return Results.NoContent();
            }
            catch (InvalidOperationException)
            {
                return Results.NotFound(new { error = new { code = "NOT_FOUND", message = "模版不存在" } });
            }
        });

        // 校验模版列
        group.MapPost("/templates/{templateId}/validate", async (Guid templateId,
            ValidateTemplateRequest request, TemplateService service) =>
        {
            var result = await service.ValidateTemplateColumnsAsync(templateId, request.BatchId);
            return Results.Ok(new ValidateTemplateResponse(
                result.Valid, result.MissingColumns,
                result.StrategyConflict, result.TemplateStrategy, result.CurrentStrategy
            ));
        });
    }
}

public record ValidateTemplateRequest(Guid BatchId);
