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
                    col.Item().AlignCenter().Text("数据分析报告").FontSize(24).Bold();
                    col.Item().AlignCenter().Text(data.Title).FontSize(14);
                    col.Item().AlignCenter().Text($"生成时间：{data.GeneratedAt:yyyy-MM-dd HH:mm}").FontSize(10);
                    col.Item().PaddingVertical(10);

                    // 汇总指标（表格上方展示）
                    if (data.SummaryMetrics?.Count > 0)
                    {
                        col.Item().Text("汇总指标").FontSize(14).Bold();
                        col.Item().PaddingVertical(4);
                        col.Item().Table(tbl =>
                        {
                            tbl.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                            foreach (var m in data.SummaryMetrics)
                            {
                                var eq = m.IndexOf('=');
                                var label = eq > 0 ? m[..eq] : m;
                                var value = eq > 0 ? m[(eq + 1)..] : "";
                                tbl.Cell().Border(1).Background(Colors.Grey.Lighten3).Padding(4).Text(label).Bold();
                                tbl.Cell().Border(1).Padding(4).Text(value);
                            }
                        });
                        col.Item().PaddingVertical(8);
                    }

                    // 计算结果表格 — 从数据中提取实际列名
                    // 从 meta 行 "分组列: 片区 | ..." 提取实际分组列名作为首列表头
                    var metaLine = data.ComputedResults?.FirstOrDefault(r => r.StartsWith("分组列:"));
                    var groupColName = metaLine != null ? metaLine.Split('|')[0].Replace("分组列:", "").Trim() : "分组";
                    var items = data.ComputedResults?.Where(r => !r.StartsWith("分组") && r != "---" && r.Contains('=')).ToList();
                    if (items?.Count > 0)
                    {
                        // 从第一行提取列名
                        var first = items[0];
                        var fi = first.IndexOf(':');
                        var headers = new List<string>();
                        if (fi > 0 && first[(fi+1)..].Contains('=')) {
                            // 分组格式: key: col1=v1, col2=v2
                            headers.Add(groupColName);
                            var afterColon = first[(fi+1)..];
                            foreach (var p in afterColon.Split(',')) { var eq = p.IndexOf('='); if (eq > 0) headers.Add(p[..eq].Trim()); }
                        } else {
                            // 多列格式: col1=v1, col2=v2
                            headers = first.Split(',').Select(p => { var eq = p.IndexOf('='); return eq > 0 ? p[..eq].Trim() : p.Trim(); }).ToList();
                        }

                        // 预解析数据
                        var isGrouped = fi > 0 && first[(fi+1)..].Contains('=');
                        var rows = items.Select(r => {
                            var ci = r.IndexOf(':');
                            if (isGrouped && ci > 0) {
                                var key = r[..ci].Trim();
                                var vals = r[(ci+1)..].Split(',').Select(p => { var eq = p.IndexOf('='); return eq > 0 ? p[(eq+1)..].Trim() : p.Trim(); }).ToList();
                                return (key, vals);
                            } else {
                                var vals = r.Split(',').Select(p => { var eq = p.IndexOf('='); return eq > 0 ? p[(eq+1)..].Trim() : p.Trim(); }).ToList();
                                return ("", vals);
                            }
                        }).ToList();

                        if (headers.Count == 2)
                            col.Item().Table(tbl => {
                                tbl.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                                tbl.Cell().Border(1).Background(Colors.Grey.Lighten3).Padding(4).Text(headers[0]).Bold();
                                tbl.Cell().Border(1).Background(Colors.Grey.Lighten3).Padding(4).Text(headers[1]).Bold();
                                foreach (var (k, v) in rows) { if(isGrouped) tbl.Cell().Border(1).Padding(4).Text(k); tbl.Cell().Border(1).Padding(4).Text(v.Count>0?v[0]:""); if(!isGrouped) tbl.Cell().Border(1).Padding(4).Text(v.Count>1?v[1]:""); }
                            });
                        else if (headers.Count == 3 && !isGrouped)
                            col.Item().Table(tbl => {
                                tbl.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); });
                                foreach(var h in headers) tbl.Cell().Border(1).Background(Colors.Grey.Lighten3).Padding(4).Text(h).Bold();
                                foreach (var (_, v) in rows) { foreach(var vv in v) tbl.Cell().Border(1).Padding(4).Text(vv); }
                            });
                        else if (headers.Count == 3)
                            col.Item().Table(tbl => {
                                tbl.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); });
                                tbl.Cell().Border(1).Background(Colors.Grey.Lighten3).Padding(4).Text(headers[0]).Bold();
                                tbl.Cell().Border(1).Background(Colors.Grey.Lighten3).Padding(4).Text(headers[1]).Bold();
                                tbl.Cell().Border(1).Background(Colors.Grey.Lighten3).Padding(4).Text(headers[2]).Bold();
                                foreach (var (k, v) in rows) { tbl.Cell().Border(1).Padding(4).Text(k); tbl.Cell().Border(1).Padding(4).Text(v.Count>0?v[0]:""); tbl.Cell().Border(1).Padding(4).Text(v.Count>1?v[1]:""); }
                            });
                        else if (headers.Count == 4)
                            col.Item().Table(tbl => {
                                tbl.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); });
                                tbl.Cell().Border(1).Background(Colors.Grey.Lighten3).Padding(4).Text(headers[0]).Bold();
                                tbl.Cell().Border(1).Background(Colors.Grey.Lighten3).Padding(4).Text(headers[1]).Bold();
                                tbl.Cell().Border(1).Background(Colors.Grey.Lighten3).Padding(4).Text(headers[2]).Bold();
                                tbl.Cell().Border(1).Background(Colors.Grey.Lighten3).Padding(4).Text(headers[3]).Bold();
                                foreach (var (k, v) in rows) { if(isGrouped)tbl.Cell().Border(1).Padding(4).Text(k); for(int i=0;i<3;i++) tbl.Cell().Border(1).Padding(4).Text(i<v.Count?v[i]:""); }
                            });
                        else
                            col.Item().Text(string.Join("\n", items.Select(r => $"  {r}"))).FontSize(11).LineHeight(1.5f);
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
    List<string>? ComputedResults = null, string? CustomRequirements = null,
    List<string>? SummaryMetrics = null
);
public record OverviewData(int RowCount, int ColumnCount, double MissingRate, string FileName);
public record StatisticRow(string ColumnName, double Mean, double Median, double Min, double Max, double StdDev);
public record CrossRow(string Dimension, string Metric, string Summary);
