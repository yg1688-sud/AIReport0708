using System.Collections.Concurrent;

namespace AIExport.Api.Infrastructure;

/// <summary>
/// 简易限流中间件：文件上传接口每用户每分钟最多 5 次请求
/// </summary>
public class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly ConcurrentDictionary<string, List<DateTime>> _requests = new();

    public RateLimitMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api/files/upload")
            && context.Request.Method == "POST")
        {
            var key = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var now = DateTime.UtcNow;
            var window = _requests.GetOrAdd(key, _ => new());

            lock (window)
            {
                window.RemoveAll(t => t < now.AddMinutes(-1));
                if (window.Count >= 5)
                {
                    context.Response.StatusCode = 429;
                    context.Response.ContentType = "application/json";
                    context.Response.WriteAsync("{\"error\":{\"code\":\"RATE_LIMITED\",\"message\":\"请求过于频繁，请稍后重试\"}}");
                    return;
                }
                window.Add(now);
            }
        }

        await _next(context);
    }
}
