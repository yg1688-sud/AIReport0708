using System.Security.Claims;
using AIExport.Api.Models.Dtos;
using AIExport.Api.Services;

namespace AIExport.Api.Endpoints;

public static class ChatEndpoints
{
    public static void MapChatEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api").RequireAuthorization();

        // 启动会话
        group.MapPost("/chat/start", async (StartChatRequest request, ChatService chat) =>
        {
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
        group.MapPost("/chat/message", async (SendMessageRequest request, ChatService chat) =>
        {
            try
            {
                var reply = await chat.SendMessageAsync(request.SessionId, request.Content);
                return Results.Ok(new ChatMessageDto(Guid.NewGuid(), "system", reply, DateTime.UtcNow));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("超时"))
            {
                return Results.Json(new { error = new { code = "SESSION_TIMEOUT", message = ex.Message } },
                    statusCode: 410);
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
            return Results.Ok(new
            {
                messages = messages.Select(m => new ChatMessageDto(m.Id,
                    m.Sender == Models.Entities.MessageSender.User ? "user" : "system",
                    m.Content, m.Timestamp))
            });
        });

        // 确认需求
        group.MapPost("/chat/confirm", async (ConfirmRequest request, ChatService chat) =>
        {
            var (success, taskId) = await chat.ConfirmRequirementsAsync(request.SessionId);
            if (!success)
                return Results.BadRequest(new { error = new { code = "NO_CONVERSATION", message = "请先描述分析需求" } });
            return Results.Ok(new ConfirmResponse(taskId, "需求已确认，正在生成报告..."));
        });
    }
}
