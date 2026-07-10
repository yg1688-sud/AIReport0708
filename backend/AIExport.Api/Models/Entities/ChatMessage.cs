namespace AIExport.Api.Models.Entities;

public enum MessageSender { User = 0, System = 1 }

public class ChatMessage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid SessionId { get; set; }
    public MessageSender Sender { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public AnalysisSession Session { get; set; } = null!;
}
