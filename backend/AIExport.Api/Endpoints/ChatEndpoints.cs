using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using AIExport.Api.Data;
using AIExport.Api.Infrastructure;
using AIExport.Api.Models.Dtos;
using AIExport.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AIExport.Api.Endpoints;

public static class ChatEndpoints
{
    public static void MapChatEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api").RequireAuthorization();

        // 启动会话
        group.MapPost("/chat/start", async (HttpContext context, ChatService chat) =>
        {
            var request = await context.Request.ReadFromJsonAsync<StartChatRequest>();
            if (request is null) return Results.BadRequest(new { error = new { code = "INVALID", message = "请求体为空" } });
            try
            {
                var strategy = request.Strategy.ToLower() == "separate"
                    ? Models.Entities.Strategy.Separate : Models.Entities.Strategy.Merge;
                var (session, firstMsg) = await chat.StartSessionAsync(request.BatchId, strategy, request.TemplateId);
                return Results.Ok(new StartChatResponse(session.Id, session.Mode.ToString().ToLower(), firstMsg));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = new { code = "INVALID", message = ex.Message } });
            }
        });

        // 发送消息
        group.MapPost("/chat/message", async (HttpContext context, ChatService chat) =>
        {
            try
            {
                var body = await context.Request.ReadFromJsonAsync<SendMessageRequest>();
                if (body is null) return Results.BadRequest(new { error = new { code = "INVALID", message = "请求体为空" } });
                var reply = await chat.SendMessageAsync(body.SessionId, body.Content, body.Model);
                string? reasoning = null; var content = reply;
                var rtag = "<!--reasoning:"; var rend = "-->";
                var ri = reply.IndexOf(rtag);
                if (ri >= 0) { var s = ri + rtag.Length; var e = reply.IndexOf(rend, s); if (e > s) { reasoning = reply[s..e]; content = reply[..ri].TrimEnd(); } }
                return Results.Ok(new ChatMessageDto(Guid.NewGuid(), "system", content, DateTime.UtcNow, reasoning));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("超时"))
            {
                return Results.Json(new { error = new { code = "SESSION_TIMEOUT", message = ex.Message } }, statusCode: 410);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = new { code = "INVALID", message = ex.Message } });
            }
        });

        // 获取历史消息
        group.MapGet("/chat/messages", async (Guid sessionId, ChatService chat) =>
        {
            var messages = await chat.GetMessagesAsync(sessionId);
            return Results.Ok(new { messages = messages.Select(m => new ChatMessageDto(m.Id, m.Sender == Models.Entities.MessageSender.User ? "user" : "system", m.Content, m.Timestamp)) });
        });

        // 确认需求
        group.MapPost("/chat/confirm", async (HttpContext context, ChatService chat) =>
        {
            var request = await context.Request.ReadFromJsonAsync<ConfirmRequest>();
            if (request is null) return Results.BadRequest(new { error = new { code = "INVALID", message = "请求体为空" } });
            var (success, taskId) = await chat.ConfirmRequirementsAsync(request.SessionId);
            if (!success) return Results.BadRequest(new { error = new { code = "NO_CONVERSATION", message = "请先描述分析需求" } });
            var queue = context.RequestServices.GetRequiredService<JobQueue>();
            await queue.EnqueueAsync(new ReportJob(taskId, request.SessionId, taskId));
            return Results.Ok(new ConfirmResponse(taskId, "需求已确认，正在生成报告..."));
        });
    }

    /// <summary>流式聊天 — 独立注册在 auth group 外部避免中间件干扰</summary>
    public static void MapChatStreamEndpoint(this WebApplication app)
    {
        app.MapPost("/api/chat/message/stream", async (HttpContext context) =>
        {
            // 手动 JWT 验证
            var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            { context.Response.StatusCode = 401; await context.Response.WriteAsync(""); return; }
            try {
                var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET") ?? "dev-secret-change-in-production-32chars!";
                new JwtSecurityTokenHandler().ValidateToken(authHeader[7..], new TokenValidationParameters
                { ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)), ValidateIssuer = false, ValidateAudience = false }, out _);
            } catch { context.Response.StatusCode = 401; await context.Response.WriteAsync(""); return; }

            var db = context.RequestServices.GetRequiredService<AppDbContext>();
            var llm = context.RequestServices.GetRequiredService<LlmClient>();
            var body = await context.Request.ReadFromJsonAsync<SendMessageRequest>();
            if (body is null) { context.Response.StatusCode = 400; return; }

            var session = await db.AnalysisSessions.Include(s => s.Batch).ThenInclude(b => b.Files).FirstOrDefaultAsync(s => s.Id == body.SessionId);
            if (session is null) { context.Response.StatusCode = 400; return; }

            db.ChatMessages.Add(new Models.Entities.ChatMessage { SessionId = body.SessionId, Sender = Models.Entities.MessageSender.User, Content = body.Content ?? "" });
            await db.SaveChangesAsync();

            var cols = new List<string>();
            foreach (var f in session.Batch?.Files?.Where(f => f.ParseStatus == Models.Entities.ParseStatus.Ready) ?? Array.Empty<Models.Entities.UploadedFile>())
            { if (f.ColumnHeaders is not null) cols.AddRange(System.Text.Json.JsonSerializer.Deserialize<List<string>>(f.ColumnHeaders) ?? new()); }

            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers["Cache-Control"] = "no-cache";
            context.Response.Headers["X-Accel-Buffering"] = "no";
            context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();

            // 获取对话历史
            var historyMessages = await db.ChatMessages
                .Where(m => m.SessionId == body.SessionId)
                .OrderBy(m => m.Timestamp)
                .ToListAsync();
            historyMessages = historyMessages.TakeLast(20).ToList();
            var history = historyMessages.Select(m => (
                role: m.Sender == Models.Entities.MessageSender.User ? "user" : "assistant",
                content: (m.Content ?? "").Split("<!--reasoning:")[0].Trim()
            )).ToList();

            string fullContent = "", reasoning = "";
            try
            {
                await foreach (var tok in llm.ChatStreamAsync(body.Content ?? "", cols.Distinct().ToArray(), body.Model, history, context.RequestAborted))
                {
                    if (tok.StartsWith("__REASONING__:"))
                    {
                        var r = tok[14..].TrimStart(':');
                        reasoning += r;
                        await context.Response.WriteAsync($"event: reasoning\ndata: {System.Text.Json.JsonSerializer.Serialize(new { text = r })}\n\n");
                    }
                    else
                    {
                        fullContent += tok;
                        await context.Response.WriteAsync($"event: token\ndata: {System.Text.Json.JsonSerializer.Serialize(new { text = tok })}\n\n");
                    }
                    await context.Response.Body.FlushAsync();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Stream] Error: {ex}");
                if (string.IsNullOrEmpty(fullContent))
                {
                    var fallback = new ChatService(db, null);
                    fullContent = await fallback.SendMessageAsync(body.SessionId, body.Content ?? "");
                    await context.Response.WriteAsync($"event: token\ndata: {System.Text.Json.JsonSerializer.Serialize(new { text = fullContent })}\n\n");
                    await context.Response.Body.FlushAsync();
                }
            }

            var saved = fullContent + (string.IsNullOrEmpty(reasoning) ? "" : $"<!--reasoning:{reasoning}-->");
            db.ChatMessages.Add(new Models.Entities.ChatMessage { SessionId = body.SessionId, Sender = Models.Entities.MessageSender.System, Content = saved });
            await db.SaveChangesAsync();
            await context.Response.WriteAsync($"event: done\ndata: {{\"reasoning\":\"{System.Text.Encodings.Web.JavaScriptEncoder.Default.Encode(reasoning)}\"}}\n\n");
            await context.Response.Body.FlushAsync();
        }).AllowAnonymous();
    }
}
