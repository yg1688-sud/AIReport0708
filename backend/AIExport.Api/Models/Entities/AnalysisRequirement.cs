namespace AIExport.Api.Models.Entities;

public class AnalysisRequirement
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid SessionId { get; set; }
    public string Dimensions { get; set; } = "[]";  // JSON array
    public string Metrics { get; set; } = "[]";     // JSON array
    public string ChartTypes { get; set; } = "[]";  // JSON array
    public string? Filters { get; set; }            // JSON object
    public string? CustomRequirements { get; set; } // 用户自定义需求描述
    public DateTime ConfirmedAt { get; set; } = DateTime.UtcNow;

    public AnalysisSession Session { get; set; } = null!;
}
