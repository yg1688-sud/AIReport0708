using System.Text.Json;
using AIExport.Api.Data;
using AIExport.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIExport.Api.Services;

public record ConsistencyResult(bool IsConsistent, List<ColumnDifference> Differences);
public record ColumnDifference(string FileName, List<string> Missing, List<string> Extra);

/// <summary>
/// 多文件策略服务：列结构一致性检查 + 策略设置
/// </summary>
public class StrategyService
{
    private readonly AppDbContext _db;

    public StrategyService(AppDbContext db) => _db = db;

    /// <summary>
    /// 检查批次中所有文件的列结构一致性（忽略大小写和首尾空格）
    /// </summary>
    public async Task<ConsistencyResult> CheckColumnConsistencyAsync(Guid batchId)
    {
        var files = await _db.UploadedFiles
            .Where(f => f.BatchId == batchId && f.ParseStatus == ParseStatus.Ready)
            .ToListAsync();

        if (files.Count <= 1)
            return new ConsistencyResult(true, new List<ColumnDifference>());

        // 以第一个文件的列为基准
        var baseline = ParseColumns(files[0].ColumnHeaders!)
            .Select(c => c.ToLowerInvariant().Trim())
            .ToArray();

        var differences = new List<ColumnDifference>();

        foreach (var file in files.Skip(1))
        {
            var cols = ParseColumns(file.ColumnHeaders!)
                .Select(c => c.ToLowerInvariant().Trim())
                .ToArray();

            var missing = baseline.Except(cols).ToList();
            var extra = cols.Except(baseline).ToList();

            if (missing.Count > 0 || extra.Count > 0)
            {
                differences.Add(new ColumnDifference(
                    file.OriginalName,
                    missing,
                    extra
                ));
            }
        }

        return new ConsistencyResult(differences.Count == 0, differences);
    }

    /// <summary>
    /// 设置批次的处理策略（合并/分别）
    /// </summary>
    public async Task SetStrategyAsync(Guid batchId, Strategy strategy)
    {
        var batch = await _db.UploadBatches.FindAsync(batchId)
            ?? throw new InvalidOperationException("批次不存在");
        batch.Strategy = strategy;
        await _db.SaveChangesAsync();
    }

    private static List<string> ParseColumns(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }
}
