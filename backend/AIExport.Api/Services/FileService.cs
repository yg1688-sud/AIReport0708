using AIExport.Api.Data;
using AIExport.Api.Infrastructure;
using AIExport.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIExport.Api.Services;

public record ValidationResult(FileFormat? Format, string? Error);
public record FileParseResult(Guid FileId, ParseStatus Status, int? RowCount, int? ColumnCount, string[]? ColumnHeaders, string? Error);

public class FileService
{
    private readonly AppDbContext _db;
    private readonly StorageService _storage;
    private readonly FileParser _parser;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".xlsx", ".xls", ".csv" };
    private const long MaxFileSize = 104_857_600; // 100MB
    private const long MaxTotalSize = 524_288_000; // 500MB
    private const int MaxFileCount = 20;

    public FileService(AppDbContext db, StorageService storage, FileParser parser)
    {
        _db = db;
        _storage = storage;
        _parser = parser;
    }

    public async Task<ValidationResult> ValidateAsync(string fileName, Stream stream, FileFormat format)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return new ValidationResult(null, "文件名为空");

        var ext = Path.GetExtension(fileName);
        if (!AllowedExtensions.Contains(ext))
            return new ValidationResult(null, $"仅支持 .xlsx、.xls 和 .csv 格式文件（不支持 {ext}）");

        if (stream.Length > MaxFileSize)
            return new ValidationResult(null, "单个文件大小不能超过 100MB");

        if (stream.Length == 0)
            return new ValidationResult(null, "文件不包含数据行，请上传包含有效数据的文件");

        return new ValidationResult(ResolveFormat(ext), null);
    }

    public async Task<UploadBatch?> GetBatchAsync(Guid batchId)
    {
        return await _db.UploadBatches.FindAsync(batchId);
    }

    public async Task<UploadBatch> CreateBatchAsync(Guid userId)
    {
        var batch = new UploadBatch
        {
            UserId = userId,
            TotalFiles = 0,
            BatchStatus = BatchStatus.Uploading
        };
        _db.UploadBatches.Add(batch);
        await _db.SaveChangesAsync();
        return batch;
    }

    public async Task<UploadedFile> AddFileAsync(Guid batchId, Guid userId, string originalName, Stream stream)
    {
        var ext = Path.GetExtension(originalName);
        var format = ResolveFormat(ext);
        var storedPath = await _storage.SaveAsync(userId, stream, originalName);

        var file = new UploadedFile
        {
            BatchId = batchId,
            OriginalName = originalName,
            StoredPath = storedPath,
            FileSize = new FileInfo(storedPath).Length,
            FileFormat = format,
            ParseStatus = ParseStatus.Parsing
        };

        _db.UploadedFiles.Add(file);

        var batch = await _db.UploadBatches.FindAsync(batchId);
        batch!.TotalFiles++;
        batch.TotalSize += file.FileSize;

        // 检查文件数量限制
        if (batch.TotalFiles > MaxFileCount)
            throw new InvalidOperationException("单次最多上传 20 个文件，请分批上传");

        // 检查总大小
        if (batch.TotalSize > MaxTotalSize)
        {
            // 建议性限制，不强制拒绝但记录
        }

        await _db.SaveChangesAsync();
        return file;
    }

    public async Task<FileParseResult> ParseFileAsync(Guid fileId, CancellationToken ct = default)
    {
        var file = await _db.UploadedFiles.FindAsync(fileId)
            ?? throw new InvalidOperationException("文件不存在");

        try
        {
            var result = await _parser.ParseAsync(file.StoredPath, file.FileFormat, ct);

            if (result.ErrorMessage is not null)
            {
                file.ParseStatus = ParseStatus.Failed;
                file.ErrorMessage = result.ErrorMessage;
                await _db.SaveChangesAsync();
                return new FileParseResult(fileId, ParseStatus.Failed, null, null, null, result.ErrorMessage);
            }

            file.RowCount = result.RowCount;
            file.ColumnCount = result.ColumnCount;
            file.ColumnHeaders = System.Text.Json.JsonSerializer.Serialize(result.ColumnHeaders);
            file.ParseStatus = ParseStatus.Ready;

            // 检查数据规模超限（单文件）
            if (result.RowCount > 1_000_000 || result.ColumnCount > 200)
            {
                file.ParseStatus = ParseStatus.Failed;
                file.ErrorMessage = "数据规模过大（超过100万行或200列），请缩减数据后重试。";
            }

            // 检查批次状态
            var batch = await _db.UploadBatches.FindAsync(file.BatchId);
            batch!.ReadyFiles++;
            if (batch.ReadyFiles == batch.TotalFiles)
                batch.BatchStatus = BatchStatus.AllReady;
            else if (batch.ReadyFiles > 0)
                batch.BatchStatus = BatchStatus.PartialReady;

            await _db.SaveChangesAsync();

            return new FileParseResult(fileId, file.ParseStatus, file.RowCount, file.ColumnCount,
                result.ColumnHeaders, null);
        }
        catch (Exception ex)
        {
            file.ParseStatus = ParseStatus.Failed;
            file.ErrorMessage = $"文件解析失败，请确认未损坏且未加密。（{ex.Message}）";
            await _db.SaveChangesAsync();
            return new FileParseResult(fileId, ParseStatus.Failed, null, null, null, file.ErrorMessage);
        }
    }

    public async Task<List<string[]>> GetPreviewAsync(Guid fileId, int maxRows = 100)
    {
        var file = await _db.UploadedFiles.FindAsync(fileId);
        if (file is null) return new List<string[]>();

        var result = await _parser.ParseAsync(file.StoredPath, file.FileFormat);
        return result.PreviewRows.Select(r => r.ToArray()).ToList();
    }

    public async Task DeleteFileAsync(Guid fileId)
    {
        var file = await _db.UploadedFiles.FindAsync(fileId);
        if (file is null) return;

        var batch = await _db.UploadBatches.FindAsync(file.BatchId);
        if (batch is not null)
        {
            batch.TotalFiles--;
            batch.TotalSize -= file.FileSize;
            if (file.ParseStatus == ParseStatus.Ready) batch.ReadyFiles--;
        }

        _storage.Delete(file.StoredPath);
        _db.UploadedFiles.Remove(file);
        await _db.SaveChangesAsync();
    }

    public async Task ClearBatchAsync(Guid batchId)
    {
        var files = await _db.UploadedFiles.Where(f => f.BatchId == batchId).ToListAsync();
        foreach (var file in files)
        {
            _storage.Delete(file.StoredPath);
            _db.UploadedFiles.Remove(file);
        }

        var batch = await _db.UploadBatches.FindAsync(batchId);
        if (batch is not null)
        {
            batch.TotalFiles = 0;
            batch.ReadyFiles = 0;
            batch.TotalSize = 0;
            batch.BatchStatus = BatchStatus.Uploading;
        }

        await _db.SaveChangesAsync();
    }

    private static FileFormat ResolveFormat(string extension) => extension.ToLower() switch
    {
        ".xlsx" => FileFormat.Xlsx,
        ".xls" => FileFormat.Xls,
        ".csv" => FileFormat.Csv,
        _ => throw new NotSupportedException($"不支持的文件格式: {extension}")
    };
}
