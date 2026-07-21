namespace AIExport.Api.Models.Entities;

public enum ReportStatus { Generating = 0, Completed = 1, Failed = 2 }
public enum ReportType { MergeReport = 0, SingleFileReport = 1 }

public class AnalysisReport
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid SessionId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public ReportStatus ReportStatus { get; set; } = ReportStatus.Generating;
    public SessionMode Mode { get; set; }
    public ReportType ReportType { get; set; }
    public string? Chapters { get; set; }           // JSON object
    public string? PdfPath { get; set; }
    public long? FileSize { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    public AnalysisSession Session { get; set; } = null!;
}
