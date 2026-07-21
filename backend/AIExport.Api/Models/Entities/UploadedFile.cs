namespace AIExport.Api.Models.Entities;

public enum FileFormat { Xlsx = 0, Xls = 1, Csv = 2 }
public enum ParseStatus { Uploading = 0, Parsing = 1, Ready = 2, Failed = 3 }

public class UploadedFile
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BatchId { get; set; }
    public string OriginalName { get; set; } = string.Empty;
    public string StoredPath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public FileFormat FileFormat { get; set; }
    public int? RowCount { get; set; }
    public int? ColumnCount { get; set; }
    public string? ColumnHeaders { get; set; } // JSON array
    public ParseStatus ParseStatus { get; set; } = ParseStatus.Uploading;
    public string? ErrorMessage { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.Now;

    public UploadBatch Batch { get; set; } = null!;
}
