namespace AIExport.Api.Services;

/// <summary>
/// 文件存储服务：按用户分目录，GUID 命名，7 天过期清理
/// </summary>
public class StorageService
{
    private readonly string _basePath;

    public StorageService(string basePath = "App_Data")
    {
        _basePath = basePath;
    }

    public string GetUserDirectory(Guid userId)
    {
        var dir = Path.Combine(_basePath, userId.ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<string> SaveAsync(Guid userId, Stream stream, string originalName, CancellationToken ct = default)
    {
        var dir = GetUserDirectory(userId);
        var ext = Path.GetExtension(originalName);
        var storedName = $"{Guid.CreateVersion7()}{ext}";
        var path = Path.Combine(dir, storedName);

        await using var fileStream = File.Create(path);
        await stream.CopyToAsync(fileStream, ct);
        return path;
    }

    public Stream OpenRead(string path)
    {
        return File.OpenRead(path);
    }

    public void Delete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>
    /// 扫描并删除过期文件（超过 retentionDays 天未修改）
    /// </summary>
    public void CleanupExpired(int retentionDays = 7)
    {
        if (!Directory.Exists(_basePath)) return;

        var cutoff = DateTime.Now.AddDays(-retentionDays);
        foreach (var file in Directory.EnumerateFiles(_basePath, "*", SearchOption.AllDirectories))
        {
            if (File.GetLastWriteTimeUtc(file) < cutoff)
                File.Delete(file);
        }
    }
}
