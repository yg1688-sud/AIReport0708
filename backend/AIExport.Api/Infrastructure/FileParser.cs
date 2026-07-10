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
    /// <summary>
    /// 解析 Excel/CSV 文件，返回：行数、列数、列头列表、前100行预览数据
    /// </summary>
    public async Task<ParseResult> ParseAsync(string filePath, FileFormat format, CancellationToken ct = default)
    {
        return format switch
        {
            FileFormat.Csv => await ParseCsvAsync(filePath, ct),
            FileFormat.Xlsx or FileFormat.Xls => ParseExcel(filePath),
            _ => throw new NotSupportedException($"不支持的文件格式: {format}")
        };
    }

    public ParseResult ParseExcel(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var sheet = workbook.Worksheet(1);

        // 检测空文件
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        if (lastRow < 2)
            return new ParseResult(0, 0, Array.Empty<string>(), new List<List<string>>(),
                "文件不包含数据行，请上传包含有效数据的 Excel 文件");

        // 读取列头（第一行）
        var headers = new List<string>();
        var lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (int col = 1; col <= lastCol; col++)
        {
            var raw = sheet.Cell(1, col).GetValue<string>();
            headers.Add(SanitizeColumnHeader(raw));
        }

        int rowCount = lastRow - 1; // 排除列头行

        // 读取前100行预览
        var preview = new List<List<string>>();
        int previewLimit = Math.Min(rowCount, 100);
        for (int row = 2; row <= previewLimit + 1; row++)
        {
            var rowData = new List<string>();
            for (int col = 1; col <= lastCol; col++)
                rowData.Add(sheet.Cell(row, col).GetValue<string>());
            preview.Add(rowData);
        }

        // 检测全缺失列
        if (headers.Count == 0 || headers.All(string.IsNullOrWhiteSpace))
            return new ParseResult(rowCount, lastCol, headers.ToArray(), preview,
                "无法生成报告：所有数据列均为空或完全缺失，请检查文件内容");

        return new ParseResult(rowCount, lastCol, headers.ToArray(), preview, null);
    }

    private async Task<ParseResult> ParseCsvAsync(string filePath, CancellationToken ct)
    {
        // 编码自动检测：尝试 UTF-8，失败则 GBK
        var encoding = DetectEncoding(filePath);

        using var reader = new StreamReader(filePath, encoding);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null
        });

        await csv.ReadAsync();
        csv.ReadHeader();
        var headers = csv.HeaderRecord?.Select(SanitizeColumnHeader).ToArray() ?? Array.Empty<string>();

        var rows = new List<List<string>>();
        int rowCount = 0;
        while (await csv.ReadAsync())
        {
            rowCount++;
            if (rowCount <= 100)
            {
                var rowData = new List<string>();
                for (int i = 0; i < headers.Length; i++)
                    rowData.Add(csv.TryGetField<string>(i, out var val) ? val : "");
                rows.Add(rowData);
            }
        }

        if (rowCount == 0)
            return new ParseResult(0, headers.Length, headers, new List<List<string>>(),
                "文件不包含数据行，请上传包含有效数据的文件");

        return new ParseResult(rowCount, headers.Length, headers, rows, null);
    }

    /// <summary>
    /// 清理列标题：移除不可打印字符、统一全角/半角、去除首尾空格
    /// </summary>
    private static string SanitizeColumnHeader(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        // 移除不可打印字符（保留常见标点）
        var cleaned = Regex.Replace(raw, @"[\p{C}]", "");
        // 全角转半角
        cleaned = Regex.Replace(cleaned, @"[！-～]", m =>
            ((char)(m.Value[0] - 0xFEE0)).ToString());
        return cleaned.Trim();
    }

    /// <summary>
    /// 检测文件编码：UTF-8 → GBK → 默认 UTF-8
    /// </summary>
    private static Encoding DetectEncoding(string filePath)
    {
        using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        reader.Peek(); // 触发编码检测
        if (reader.CurrentEncoding is UTF8Encoding) return Encoding.UTF8;

        try
        {
            using var gbkReader = new StreamReader(filePath, Encoding.GetEncoding("GBK"));
            gbkReader.Peek();
            return Encoding.GetEncoding("GBK");
        }
        catch
        {
            return Encoding.UTF8;
        }
    }
}

public record ParseResult(
    int RowCount,
    int ColumnCount,
    string[] ColumnHeaders,
    List<List<string>> PreviewRows,
    string? ErrorMessage
);
