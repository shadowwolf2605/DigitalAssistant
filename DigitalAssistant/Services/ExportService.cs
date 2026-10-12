using DigitalAssistant.Models;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DigitalAssistant.Services;

// Изтегляне на справките като .xls и .pdf файл (т. 3.1.4)
public class ExportService
{
    public const string Organization = "„Административен информационен център“ ЕООД";

    public byte[] ToXls(ReportTable table)
    {
        var workbook = new HSSFWorkbook();
        var sheet = workbook.CreateSheet("Справка");

        var bold = workbook.CreateFont();
        bold.IsBold = true;
        var boldStyle = workbook.CreateCellStyle();
        boldStyle.SetFont(bold);

        var header = workbook.CreateCellStyle();
        header.SetFont(bold);
        header.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Grey25Percent.Index;
        header.FillPattern = FillPattern.SolidForeground;
        header.BorderBottom = header.BorderTop = header.BorderLeft = header.BorderRight = BorderStyle.Thin;

        var cell = workbook.CreateCellStyle();
        cell.BorderBottom = cell.BorderTop = cell.BorderLeft = cell.BorderRight = BorderStyle.Thin;
        cell.WrapText = true;
        cell.VerticalAlignment = NPOI.SS.UserModel.VerticalAlignment.Top;
        // Суми като таксите се показват с два знака след точката (2.50, а не 2.5)
        var money = workbook.CreateCellStyle();
        money.CloneStyleFrom(cell);
        money.DataFormat = workbook.CreateDataFormat().GetFormat("0.00");

        int r = 0;
        var titleCell = sheet.CreateRow(r++).CreateCell(0);
        titleCell.SetCellValue(table.Title);
        titleCell.CellStyle = boldStyle;
        sheet.CreateRow(r++).CreateCell(0).SetCellValue(table.Subtitle);
        r++;

        var headerRow = sheet.CreateRow(r++);
        for (int c = 0; c < table.Headers.Count; c++)
        {
            var hc = headerRow.CreateCell(c);
            hc.SetCellValue(table.Headers[c]);
            hc.CellStyle = header;
        }

        foreach (var row in table.Rows)
        {
            var excelRow = sheet.CreateRow(r++);
            for (int c = 0; c < row.Count; c++)
            {
                var ec = excelRow.CreateCell(c);
                // Числата се записват като числа, за да може да се смятат в Excel
                if (double.TryParse(row[c], System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.InvariantCulture, out var number) && row[c].Length < 12)
                {
                    ec.SetCellValue(number);
                    ec.CellStyle = row[c].Contains('.') ? money : cell;
                }
                else
                {
                    ec.SetCellValue(row[c]);
                    ec.CellStyle = cell;
                }
            }
        }

        if (table.Footer != null)
        {
            r++;
            sheet.CreateRow(r).CreateCell(0).SetCellValue(table.Footer);
        }

        // Ширина на колоните според най-дългия текст
        for (int c = 0; c < table.Headers.Count; c++)
        {
            int max = table.Headers[c].Length;
            foreach (var row in table.Rows) max = Math.Max(max, Math.Min(row[c].Length, 60));
            sheet.SetColumnWidth(c, Math.Min(255, max + 3) * 256);
        }

        using var ms = new MemoryStream();
        workbook.Write(ms);
        return ms.ToArray();
    }

    public byte[] ToPdf(ReportTable table)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(table.Headers.Count > 4 ? PageSizes.A4.Landscape() : PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text(Organization).FontSize(9).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(4).Text(table.Title).FontSize(16).Bold();
                    col.Item().Text(table.Subtitle).FontSize(10);
                    col.Item().PaddingBottom(10);
                });

                page.Content().Column(col =>
                {
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(cd =>
                        {
                            // Колоната "№" е тясна, а останалите си делят мястото
                            foreach (var h in table.Headers)
                                if (h == "№") cd.ConstantColumn(30); else cd.RelativeColumn();
                        });

                        t.Header(h =>
                        {
                            foreach (var title in table.Headers)
                                h.Cell().Background(Colors.Grey.Lighten2).Border(0.5f).Padding(4).Text(title).Bold();
                        });

                        foreach (var row in table.Rows)
                            foreach (var value in row)
                                t.Cell().Border(0.5f).Padding(4).Text(value);
                    });

                    if (table.Rows.Count == 0)
                        col.Item().PaddingTop(8).Text("Няма данни за избрания период.");
                    if (table.Footer != null)
                        col.Item().PaddingTop(8).Text(table.Footer).Bold();
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text($"Изготвена на {DateTime.Now:dd.MM.yyyy HH:mm}").FontSize(8);
                    row.RelativeItem().AlignRight().Text(x =>
                    {
                        x.Span("Страница ").FontSize(8);
                        x.CurrentPageNumber().FontSize(8);
                        x.Span(" от ").FontSize(8);
                        x.TotalPages().FontSize(8);
                    });
                });
            });
        });
        return document.GeneratePdf();
    }

    // Потвърждение за подадено онлайн заявление (гражданинът може да го изтегли и разпечата)
    public byte[] ApplicationReceipt(Application app, string egnMasked, DateTime? expected)
    {
        var citizen = app.Citizen!;
        var service = app.Service!;
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(48);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header().Column(col =>
                {
                    col.Item().Text(Organization).FontSize(9).FontColor(Colors.Grey.Darken1);
                    col.Item().Text("гр. Русе").FontSize(9).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(16).Text("ПОТВЪРЖДЕНИЕ ЗА ПОДАДЕНО ЗАЯВЛЕНИЕ").FontSize(16).Bold();
                    col.Item().Text($"№ {app.Number} от {app.CreatedAt:dd.MM.yyyy} г., {app.CreatedAt:HH:mm} ч.").FontSize(12);
                    col.Item().PaddingBottom(16);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(6);
                    void Row(string label, string value) => col.Item().Row(r =>
                    {
                        r.ConstantItem(170).Text(label).SemiBold();
                        r.RelativeItem().Text(value);
                    });
                    Row("Заявител:", citizen.FullName);
                    Row("ЕГН:", egnMasked);
                    Row("Имейл:", citizen.Email);
                    Row("Телефон:", citizen.Phone);
                    col.Item().PaddingVertical(6).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                    Row("Услуга:", service.Name);
                    Row("Институция:", service.Institution?.Name ?? "");
                    Row("Какво се заявява:", app.Details);
                    Row("Получаване на резултата:", app.ResultDelivery + (app.DeliveryAddress.Length > 0 ? $" – {app.DeliveryAddress}" : ""));
                    Row("Приложени документи:", string.Join(", ", app.Files.Where(f => !f.ByStaff).OrderBy(f => f.UploadedAt).Select(f => f.FileName)));
                    Row("Такса:", app.Fee == 0 ? "безплатно" : $"{app.Fee:0.00} € (плаща се на гишето или по банков път)");
                    Row("Текущ статус:", ApplicationStatuses.Name(app.Status));
                    if (expected != null) Row("Очаквано готово до:", $"{expected:dd.MM.yyyy} г. ({WorkingDays.Text(app.DeadlineDays)})");
                    col.Item().PaddingTop(20).Text("Статусът на заявлението и съобщенията от служителя се виждат в „Моите заявления“ на сайта. " +
                        (citizen.EmailNotifications ? "При промяна по заявлението ще получите и имейл." : "")).FontSize(10).FontColor(Colors.Grey.Darken2);
                });

                page.Footer().Text($"Документът е създаден автоматично на {DateTime.Now:dd.MM.yyyy HH:mm} и не се подписва.").FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        });
        return document.GeneratePdf();
    }
}
