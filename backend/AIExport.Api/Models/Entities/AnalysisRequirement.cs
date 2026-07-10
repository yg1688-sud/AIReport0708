namespace AIExport.Api.Models.Entities;

public class AnalysisRequirement
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid SessionId { get; set; }
    public string Dimensions { get; set; } = "[]";  // JSON array
    public string Metrics { get; set; } = "[]";     // JSON array
    public string ChartTypes { get; set; } = "[]";  // JSON array
    public string? Filters { get; set; }            // JSON object
    public DateTime ConfirmedAt { get; set; } = DateTime.UtcNow;

    public AnalysisSession Session { get; set; } = null!;
}
