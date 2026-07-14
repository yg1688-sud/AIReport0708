using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AIExport.Api.Infrastructure;

public class PdfGenerator
{
    static PdfGenerator() { QuestPDF.Settings.License = LicenseType.Community; }

    public byte[] Generate(ReportData data)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4); page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(11).FontFamily("SimSun"));

                page.Content().Column(col =>
                {
                    col.Item().AlignCenter().Text("数据分析报告").FontSize(24).Bold().FontColor(Colors.Blue.Darken3);
                    col.Item().AlignCenter().Text(data.Title).FontSize(14);
                    col.Item().AlignCenter().Text($"生成时间：{data.GeneratedAt:yyyy-MM-dd HH:mm}").FontSize(10);
                    col.Item().PaddingVertical(10);

                    // 自定义需求
                    if (!string.IsNullOrEmpty(data.CustomRequirements))
                    {
                        col.Item().Text("确认的分析需求").FontSize(16).Bold();
                        col.Item().Text(data.CustomRequirements).FontSize(10);
                        col.Item().PaddingVertical(10);
                    }

                    // 计算结果优先
                    if (data.ComputedResults?.Count > 0)
                    {
                        col.Item().Text("计算结果").FontSize(16).Bold();
                        col.Item().Table(tbl =>
                        {
                            tbl.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                            tbl.Header(h => { h.Cell().Text("指标").Bold(); h.Cell().Text("数值").Bold(); });
                            foreach (var r in data.ComputedResults)
                            {
                                var idx = r.IndexOf(':');
                                tbl.Cell().Text(idx > 0 ? r[..idx] : r);
                                tbl.Cell().Text(idx > 0 ? r[(idx + 1)..] : "");
                            }
                        });
                        col.Item().PaddingVertical(10);
                    }

                    // 分析结论
                    if (!string.IsNullOrEmpty(data.AnalysisText))
                    {
                        col.Item().Text("分析结论").FontSize(16).Bold();
                        col.Item().Text(data.AnalysisText).FontSize(11);
                        col.Item().PaddingVertical(10);
                    }

                    // 仅在无自定义结果时展示概览+统计
                    if (data.ComputedResults is null || data.ComputedResults.Count == 0)
                    {
                        col.Item().Text("数据概览").FontSize(16).Bold();
                        col.Item().Text($"总行数：{data.Overview.RowCount:N0}  总列数：{data.Overview.ColumnCount}");
                        col.Item().PaddingVertical(5);

                        if (data.Statistics.Count > 0)
                        {
                            col.Item().Text("描述性统计").FontSize(16).Bold();
                            col.Item().Table(tbl =>
                            {
                                tbl.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); });
                                tbl.Header(h => { h.Cell().Text("列名").Bold(); h.Cell().Text("均值").Bold(); h.Cell().Text("中位数").Bold(); h.Cell().Text("最小值").Bold(); h.Cell().Text("最大值").Bold(); });
                                foreach (var s in data.Statistics) { tbl.Cell().Text(s.ColumnName); tbl.Cell().Text(s.Mean.ToString("F2")); tbl.Cell().Text(s.Median.ToString("F2")); tbl.Cell().Text(s.Min.ToString("F2")); tbl.Cell().Text(s.Max.ToString("F2")); }
                            });
                            col.Item().PaddingVertical(10);
                        }
                    }

                    // 交叉分析
                    if (data.CrossAnalysis.Count > 0)
                    {
                        col.Item().Text("交叉分析").FontSize(16).Bold();
                        foreach (var cross in data.CrossAnalysis)
                            col.Item().Text($"  {cross.Dimension} × {cross.Metric}: {cross.Summary}");
                    }
                });

                page.Footer().AlignCenter().Text("AIExport 数据分析报告系统");
            });
        }).GeneratePdf();
    }
}

public record ReportData(
    string Title, DateTime GeneratedAt, OverviewData Overview, List<StatisticRow> Statistics,
    List<string> ChartTypes, List<CrossRow> CrossAnalysis, string AnalysisText = "",
    List<string>? ComputedResults = null, string? CustomRequirements = null
);
public record OverviewData(int RowCount, int ColumnCount, double MissingRate, string FileName);
public record StatisticRow(string ColumnName, double Mean, double Median, double Min, double Max, double StdDev);
public record CrossRow(string Dimension, string Metric, string Summary);
