using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using AIExport.Api.Models.Entities;

namespace AIExport.Api.Infrastructure;

public class FileParser
{
    public async Task<ParseResult> ParseAsync(string filePath, FileFormat format, CancellationToken ct = default)
    {
        return format switch
        {
            FileFormat.Csv => await ParseCsvAsync(filePath, ct, 100),
            FileFormat.Xlsx or FileFormat.Xls => ParseExcel(filePath, 100),
            _ => throw new NotSupportedException($"不支持的文件格式: {format}")
        };
    }

    public async Task<ParseResult> ParseAllAsync(string filePath, FileFormat format, CancellationToken ct = default)
    {
        return format switch
        {
            FileFormat.Csv => await ParseCsvAsync(filePath, ct, int.MaxValue),
            FileFormat.Xlsx or FileFormat.Xls => ParseExcel(filePath, int.MaxValue),
            _ => throw new NotSupportedException($"不支持的文件格式: {format}")
        };
    }

    public ParseResult ParseExcel(string filePath, int maxPreview = 100)
    {
        using var workbook = new XLWorkbook(filePath);
        var sheet = workbook.Worksheet(1);
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        if (lastRow < 2)
            return new ParseResult(0, 0, Array.Empty<string>(), new List<List<string>>(), "文件不包含数据行");

        var headers = new List<string>();
        var lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (int col = 1; col <= lastCol; col++)
            headers.Add(SanitizeColumnHeader(sheet.Cell(1, col).GetValue<string>()));

        int rowCount = lastRow - 1;
        var preview = new List<List<string>>();
        int limit = Math.Min(rowCount, maxPreview);
        for (int row = 2; row <= limit + 1; row++)
        {
            var rowData = new List<string>();
            for (int col = 1; col <= lastCol; col++)
                rowData.Add(sheet.Cell(row, col).GetValue<string>());
            preview.Add(rowData);
        }

        if (headers.Count == 0 || headers.All(string.IsNullOrWhiteSpace))
            return new ParseResult(rowCount, lastCol, headers.ToArray(), preview, "所有数据列均为空");

        return new ParseResult(rowCount, lastCol, headers.ToArray(), preview, null);
    }

    private async Task<ParseResult> ParseCsvAsync(string filePath, CancellationToken ct, int maxPreview = 100)
    {
        var encoding = DetectEncoding(filePath);
        using var reader = new StreamReader(filePath, encoding);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        { HasHeaderRecord = true, BadDataFound = null, MissingFieldFound = null });

        await csv.ReadAsync();
        csv.ReadHeader();
        var headers = csv.HeaderRecord?.Select(SanitizeColumnHeader).ToArray() ?? Array.Empty<string>();

        var rows = new List<List<string>>();
        int rowCount = 0;
        while (await csv.ReadAsync())
        {
            rowCount++;
            if (rowCount <= maxPreview)
            {
                var rowData = new List<string>();
                for (int i = 0; i < headers.Length; i++)
                    rowData.Add(csv.TryGetField<string>(i, out var val) ? val : "");
                rows.Add(rowData);
            }
        }

        if (rowCount == 0)
            return new ParseResult(0, headers.Length, headers, new List<List<string>>(), "文件不包含数据行");

        return new ParseResult(rowCount, headers.Length, headers, rows, null);
    }

    private static string SanitizeColumnHeader(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var cleaned = Regex.Replace(raw, @"[\p{C}]", "");
        cleaned = Regex.Replace(cleaned, @"[！-～]", m => ((char)(m.Value[0] - 0xFEE0)).ToString());
        return cleaned.Trim();
    }

    private static Encoding DetectEncoding(string filePath)
    {
        using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        reader.Peek();
        if (reader.CurrentEncoding is UTF8Encoding) return Encoding.UTF8;
        try { using var gbk = new StreamReader(filePath, Encoding.GetEncoding("GBK")); gbk.Peek(); return Encoding.GetEncoding("GBK"); }
        catch { return Encoding.UTF8; }
    }
}

public record ParseResult(int RowCount, int ColumnCount, string[] ColumnHeaders, List<List<string>> PreviewRows, string? ErrorMessage);
