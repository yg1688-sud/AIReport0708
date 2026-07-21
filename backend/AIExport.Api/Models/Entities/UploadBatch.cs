namespace AIExport.Api.Models.Entities;

public enum BatchStatus { Uploading = 0, PartialReady = 1, AllReady = 2 }
public enum Strategy { Merge = 0, Separate = 1 }

public class UploadBatch
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public int TotalFiles { get; set; }
    public int ReadyFiles { get; set; }
    public long TotalSize { get; set; }
    public BatchStatus BatchStatus { get; set; } = BatchStatus.Uploading;
    public Strategy? Strategy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public User User { get; set; } = null!;
    public ICollection<UploadedFile> Files { get; set; } = new List<UploadedFile>();
    public ICollection<AnalysisSession> Sessions { get; set; } = new List<AnalysisSession>();
}
