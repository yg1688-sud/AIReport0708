using AIExport.Api.Models.Dtos;
using AIExport.Api.Services;
using FluentValidation;

namespace AIExport.Api.Endpoints;

/// <summary>
/// 认证相关端点
/// </summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/login", async (LoginRequest request, AuthService authService) =>
        {
            // 输入验证
            var validator = new LoginRequestValidator();
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                return Results.ValidationProblem(validation.ToDictionary());
            }

            try
            {
                var result = await authService.LoginAsync(request.Username, request.Password);
                return Results.Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = new { code = "AUTH_FAILED", message = ex.Message } },
                    statusCode: 401);
            }
        }).AllowAnonymous();
    }
}
