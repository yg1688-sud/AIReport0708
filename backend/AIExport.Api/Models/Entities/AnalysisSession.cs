namespace AIExport.Api.Models.Entities;

public enum SessionStatus { Chatting = 0, Confirmed = 1, Expired = 2, Timeout = 3 }
public enum SessionMode { Chat = 0, Template = 1 }

public class AnalysisSession
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BatchId { get; set; }
    public Guid? TemplateId { get; set; }
    public SessionStatus SessionStatus { get; set; } = SessionStatus.Chatting;
    public SessionMode Mode { get; set; } = SessionMode.Chat;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    public UploadBatch Batch { get; set; } = null!;
    public AnalysisTemplate? Template { get; set; }
    public AnalysisRequirement? Requirement { get; set; }
    public AnalysisReport? Report { get; set; }
    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
