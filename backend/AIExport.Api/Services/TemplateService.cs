using System.Text.Json;
using AIExport.Api.Data;
using AIExport.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIExport.Api.Services;

public record TemplateValidationResult(bool Valid, List<string> MissingColumns,
    bool StrategyConflict, string? TemplateStrategy, string? CurrentStrategy);

public class TemplateService
{
    private readonly AppDbContext _db;
    public TemplateService(AppDbContext db) => _db = db;

    public async Task<List<AnalysisTemplate>> GetUserTemplatesAsync(Guid userId)
    {
        return await _db.AnalysisTemplates
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<AnalysisTemplate> SaveTemplateAsync(Guid sessionId, Guid userId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("模版名称不能为空");

        // 检查名称重复
        var existing = await _db.AnalysisTemplates
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Name == name);
        if (existing is not null)
            throw new InvalidOperationException($"模版名称已存在：{name}");

        var session = await _db.AnalysisSessions
            .Include(s => s.Requirement)
            .Include(s => s.Batch).ThenInclude(b => b.Files)
            .FirstOrDefaultAsync(s => s.Id == sessionId)
            ?? throw new InvalidOperationException("会话不存在");

        var req = session.Requirement
            ?? throw new InvalidOperationException("需求不存在");

        // 提取关联列名
        var firstFile = session.Batch.Files.FirstOrDefault();
        var columnNames = firstFile?.ColumnHeaders ?? "[]";

        var template = new AnalysisTemplate
        {
            UserId = userId,
            Name = name,
            Dimensions = req.Dimensions,
            Metrics = req.Metrics,
            ChartTypes = req.ChartTypes,
            Filters = req.Filters,
            ColumnNames = columnNames,
            CustomRequirements = req.CustomRequirements,
            Strategy = session.Batch.Strategy ?? Strategy.Merge
        };

        _db.AnalysisTemplates.Add(template);
        await _db.SaveChangesAsync();
        return template;
    }

    public async Task DeleteTemplateAsync(Guid templateId, Guid userId)
    {
        var template = await _db.AnalysisTemplates
            .FirstOrDefaultAsync(t => t.Id == templateId && t.UserId == userId)
            ?? throw new InvalidOperationException("模版不存在");
        // 解除关联会话的模版引用
        var sessions = await _db.AnalysisSessions.Where(s => s.TemplateId == templateId).ToListAsync();
        foreach (var s in sessions) s.TemplateId = null;
        _db.AnalysisTemplates.Remove(template);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// 校验模版列是否在当前批次文件中存在 + 策略冲突检测
    /// </summary>
    public async Task<TemplateValidationResult> ValidateTemplateColumnsAsync(Guid templateId, Guid batchId)
    {
        var template = await _db.AnalysisTemplates.FindAsync(templateId)
            ?? throw new InvalidOperationException("模版不存在");

        var batch = await _db.UploadBatches
            .Include(b => b.Files)
            .FirstOrDefaultAsync(b => b.Id == batchId)
            ?? throw new InvalidOperationException("批次不存在");

        // 收集所有文件的所有列
        var allColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in batch.Files.Where(f => f.ParseStatus == ParseStatus.Ready))
        {
            if (file.ColumnHeaders is null) continue;
            foreach (var col in ParseJsonArray(file.ColumnHeaders))
                allColumns.Add(col.Trim());
        }

        var requiredColumns = ParseJsonArray(template.ColumnNames);
        var missing = requiredColumns.Where(c => !allColumns.Contains(c.Trim())).ToList();

        var strategyConflict = batch.Strategy.HasValue && batch.Strategy != template.Strategy;

        return new TemplateValidationResult(
            Valid: missing.Count == 0 && !strategyConflict,
            MissingColumns: missing,
            StrategyConflict: strategyConflict,
            TemplateStrategy: template.Strategy.ToString().ToLower(),
            CurrentStrategy: batch.Strategy?.ToString().ToLower()
        );
    }

    private static List<string> ParseJsonArray(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch { return new(); }
    }
}
