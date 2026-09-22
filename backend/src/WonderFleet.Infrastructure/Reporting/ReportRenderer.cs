using System.Globalization;
using System.Text;
using CsvHelper;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WonderFleet.Application.Common.Interfaces;

namespace WonderFleet.Infrastructure.Reporting;

/// PDF via QuestPDF (landscape, repeating header) and CSV via CsvHelper with formula-injection guards.
internal sealed class ReportRenderer : IReportRenderer
{
    private const string Ink = "#1F2A24";
    private const string Brand = "#0F5132";
    private const string Muted = "#5C6B63";
    private const string Line = "#E6ECE9";

    public ReportFile RenderPdf(ReportTable table, string fileName)
    {
        var bytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.DefaultTextStyle(x => x.FontSize(8).FontColor(Ink).FontFamily(Fonts.Calibri));

                page.Header().Column(header =>
                {
                    header.Item().Text("WonderFleet").FontSize(16).SemiBold().FontColor(Brand);
                    header.Item().Text(table.Title).FontSize(12).SemiBold();
                    header.Item().Text(table.Subtitle).FontSize(8).FontColor(Muted);
                    header.Item().PaddingTop(6).LineHorizontal(0.8f).LineColor(Line);
                });

                page.Content().PaddingVertical(8).Column(column =>
                {
                    if (table.Highlights.Count > 0)
                    {
                        column.Item().PaddingBottom(8).Row(row =>
                        {
                            foreach (var (label, value) in table.Highlights)
                            {
                                row.RelativeItem().Border(0.8f).BorderColor(Line).Padding(6).Column(card =>
                                {
                                    card.Item().Text(label).FontSize(7).FontColor(Muted);
                                    card.Item().Text(value).FontSize(11).SemiBold().FontColor(Brand);
                                });
                                row.ConstantItem(6);
                            }
                        });
                    }

                    if (table.Rows.Count == 0)
                    {
                        column.Item().PaddingTop(20).AlignCenter().Text("No data for this period.").FontColor(Muted);
                        return;
                    }

                    column.Item().Table(grid =>
                    {
                        grid.ColumnsDefinition(columns =>
                        {
                            foreach (var _ in table.Columns) columns.RelativeColumn();
                        });

                        grid.Header(header =>
                        {
                            foreach (var name in table.Columns)
                                header.Cell().Background("#F2F7F4").PaddingVertical(4).PaddingHorizontal(3)
                                    .Text(name).SemiBold().FontSize(8).FontColor(Brand);
                        });

                        var index = 0;
                        foreach (var row in table.Rows)
                        {
                            var background = index++ % 2 == 0 ? "#FFFFFF" : "#FAFCFB"; // both strings: Color<->string ternary is ambiguous
                            foreach (var cell in row)
                                grid.Cell().Background(background).BorderBottom(0.5f).BorderColor(Line)
                                    .PaddingVertical(3).PaddingHorizontal(3).Text(cell).FontSize(7.5f);
                        }
                    });
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text("WonderFleet by OfeminiAgricTech · Ofemini Global Limited").FontSize(7).FontColor(Muted);
                    row.ConstantItem(120).AlignRight().Text(text =>
                    {
                        text.DefaultTextStyle(x => x.FontSize(7).FontColor(Muted));
                        text.Span("Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
                });
            });
        }).GeneratePdf();

        return new ReportFile(bytes, "application/pdf", fileName);
    }

    public ReportFile RenderCsv(ReportTable table, string fileName)
    {
        using var buffer = new MemoryStream();
        // UTF-8 BOM so Excel opens the "°C" and "→" characters correctly.
        using (var writer = new StreamWriter(buffer, new UTF8Encoding(true), leaveOpen: true))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            foreach (var column in table.Columns) csv.WriteField(column);
            csv.NextRecord();
            foreach (var row in table.Rows)
            {
                foreach (var cell in row) csv.WriteField(Sanitize(cell));
                csv.NextRecord();
            }
            if (table.Highlights.Count > 0)
            {
                csv.NextRecord();
                foreach (var (label, value) in table.Highlights)
                {
                    csv.WriteField(Sanitize(label));
                    csv.WriteField(Sanitize(value));
                    csv.NextRecord();
                }
            }
        }
        return new ReportFile(buffer.ToArray(), "text/csv", fileName);
    }

    /// Neutralises spreadsheet formula injection (=, +, -, @, tab, CR).
    internal static string Sanitize(string value) =>
        value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
}
