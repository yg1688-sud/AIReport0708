using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AIExport.Api.Infrastructure;

/// <summary>
/// PDF 报告生成器 — QuestPDF 模板：封面 → 数据概览 → 描述性统计 → 图表 → 交叉分析
/// </summary>
public class PdfGenerator
{
    static PdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(ReportData data)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(11).FontFamily("SimSun"));

                // 封面
                page.Content().Column(col =>
                {
                    col.Item().AlignCenter().Text("数据分析报告")
                        .FontSize(24).Bold().FontColor(Colors.Blue.Darken3);
                    col.Item().AlignCenter().Text(data.Title).FontSize(14);
                    col.Item().AlignCenter().Text($"生成时间：{data.GeneratedAt:yyyy-MM-dd HH:mm}").FontSize(10);
                    col.Item().PaddingVertical(20);

                    // 数据概览
                    col.Item().Text("一、数据概览").FontSize(16).Bold();
                    col.Item().Text($"总行数：{data.Overview.RowCount:N0}  总列数：{data.Overview.ColumnCount}");
                    col.Item().Text($"缺失率：{data.Overview.MissingRate:P1}  数据文件：{data.Overview.FileName}");
                    col.Item().PaddingVertical(10);

                    // 描述性统计
                    col.Item().Text("二、描述性统计").FontSize(16).Bold();
                    foreach (var stat in data.Statistics)
                    {
                        col.Item().Text($"  {stat.ColumnName}: 均值={stat.Mean:F2}, 中位数={stat.Median:F2}, 最小值={stat.Min:F2}, 最大值={stat.Max:F2}, 标准差={stat.StdDev:F2}");
                    }
                    col.Item().PaddingVertical(10);

                    // 图表分析
                    col.Item().Text("三、图表分析").FontSize(16).Bold();
                    col.Item().Text($"图表类型：{string.Join("、", data.ChartTypes)}");
                    col.Item().Text("（图表详情请在网页端查看交互式图表）");
                    col.Item().PaddingVertical(10);

                    // 交叉分析
                    col.Item().Text("四、交叉分析").FontSize(16).Bold();
                    foreach (var cross in data.CrossAnalysis)
                    {
                        col.Item().Text($"  {cross.Dimension} × {cross.Metric}: {cross.Summary}");
                    }
                });

                // 页脚
                page.Footer().AlignCenter().Text("AIExport 数据分析报告系统");
            });
        }).GeneratePdf();
    }
}

public record ReportData(
    string Title,
    DateTime GeneratedAt,
    OverviewData Overview,
    List<StatisticRow> Statistics,
    List<string> ChartTypes,
    List<CrossRow> CrossAnalysis
);

public record OverviewData(int RowCount, int ColumnCount, double MissingRate, string FileName);
public record StatisticRow(string ColumnName, double Mean, double Median, double Min, double Max, double StdDev);
public record CrossRow(string Dimension, string Metric, string Summary);
