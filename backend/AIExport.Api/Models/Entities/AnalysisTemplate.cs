namespace AIExport.Api.Models.Entities;

public class AnalysisTemplate
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Dimensions { get; set; } = "[]";   // JSON array
    public string Metrics { get; set; } = "[]";      // JSON array
    public string ChartTypes { get; set; } = "[]";   // JSON array
    public string? Filters { get; set; }             // JSON object
    public string ColumnNames { get; set; } = "[]";  // JSON array — 用于列校验
    public Strategy Strategy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public ICollection<AnalysisSession> Sessions { get; set; } = new List<AnalysisSession>();
}
