using System.Globalization;
using Dph.Core.Domain;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace Dph.Core.Invoicing;

// Vykreslí vydanou fakturu (TaxSubject = dodavatel, IssuedInvoice = doklad) do PDF s QR Platbou.
public sealed class InvoicePdfRenderer
{
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");
    private const string FontFamily = EmbeddedFontResolver.FamilyName;

    public void Render(TaxSubject supplier, IssuedInvoice invoice, string targetPath)
    {
        EmbeddedFontResolver.EnsureRegistered();

        var document = BuildDocument(supplier, invoice);
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        renderer.Save(targetPath);
    }

    // QR Platba potřebuje IBAN; vezmeme explicitní z poplatníka, jinak dopočítáme z čísla účtu.
    // Vrací obrázek QR jako "base64:" zdroj pro MigraDoc (BMP, který PDFsharp umí dekódovat),
    // nebo null když IBAN není k dispozici.
    private static string? TryBuildQrSource(TaxSubject supplier, IssuedInvoice invoice)
    {
        var iban = supplier.Iban.NullOrTrim() ?? CzechIban.TryFromAccount(supplier.BankAccount);
        if (iban is null)
        {
            return null;
        }

        var spayd = SpaydBuilder.Build(
            iban,
            invoice.TotalGrossCzk,
            invoice.Currency,
            invoice.PaymentVariableSymbol,
            message: null);
        return "base64:" + Convert.ToBase64String(QrCodeRenderer.RenderBmp(spayd));
    }

    private static Document BuildDocument(TaxSubject supplier, IssuedInvoice invoice)
    {
        var document = new Document();
        document.Styles["Normal"]!.Font.Name = FontFamily;
        document.Styles["Normal"]!.Font.Size = 10;

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.LeftMargin = Unit.FromCentimeter(2);
        section.PageSetup.RightMargin = Unit.FromCentimeter(2);
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.8);

        var title = section.AddParagraph($"Faktura č. {invoice.Number}");
        title.Format.Font.Size = 18;
        title.Format.Font.Bold = true;
        title.Format.SpaceAfter = Unit.FromMillimeter(6);

        AddParties(section, supplier, invoice);
        AddMeta(section, supplier, invoice);
        AddIntro(section, invoice);
        AddItemsTable(section, invoice);
        AddRecap(section, invoice);
        AddFooter(section, invoice);

        return document;
    }

    private static void AddIntro(Section section, IssuedInvoice invoice)
    {
        if (string.IsNullOrWhiteSpace(invoice.IntroText))
        {
            return;
        }

        // Placeholdery {měsíc}/{rok} se dosadí podle DUZP, ať text vždy sedí na zdaňovací období.
        var text = InvoiceText.ResolvePlaceholders(invoice.IntroText, invoice.TaxableSupplyDate);
        var intro = section.AddParagraph(text);
        SetBodyLineSpacing(intro);
        intro.Format.SpaceAfter = Unit.FromMillimeter(3);
    }

    // Hlavička: vlevo dodavatel, uprostřed odběratel, vpravo nahoře QR Platba.
    private static void AddParties(Section section, TaxSubject supplier, IssuedInvoice invoice)
    {
        var table = section.AddTable();
        table.AddColumn(Unit.FromCentimeter(7));
        table.AddColumn(Unit.FromCentimeter(6.5));
        table.AddColumn(Unit.FromCentimeter(3.5));
        var row = table.AddRow();

        var left = row.Cells[0].AddParagraph();
        SetBodyLineSpacing(left);
        AddLabel(left, "DODAVATEL");
        AddLine(left, SupplierName(supplier), bold: true);
        AddLine(left, JoinAddress(supplier.Street, supplier.HouseNumber));
        AddLine(left, JoinCity(supplier.PostalCode, supplier.City));
        if (!string.IsNullOrWhiteSpace(supplier.Ico)) AddLine(left, $"IČO: {supplier.Ico}");
        if (!string.IsNullOrWhiteSpace(supplier.Dic)) AddLine(left, $"DIČ: {supplier.Dic}");

        var middle = row.Cells[1].AddParagraph();
        SetBodyLineSpacing(middle);
        AddLabel(middle, "ODBĚRATEL");
        AddLine(middle, invoice.CustomerName, bold: true);
        AddLine(middle, JoinAddress(invoice.CustomerStreet, invoice.CustomerHouseNumber));
        AddLine(middle, JoinCity(invoice.CustomerPostalCode, invoice.CustomerCity));
        AddLine(middle, invoice.CustomerCountry);
        if (!string.IsNullOrWhiteSpace(invoice.CustomerIco)) AddLine(middle, $"IČO: {invoice.CustomerIco}");
        if (!string.IsNullOrWhiteSpace(invoice.CustomerDic)) AddLine(middle, $"DIČ: {invoice.CustomerDic}");

        var qrSource = TryBuildQrSource(supplier, invoice);
        if (qrSource is not null)
        {
            var qrCell = row.Cells[2].AddParagraph();
            qrCell.Format.Alignment = ParagraphAlignment.Center;
            AddLabel(qrCell, "QR PLATBA");
            var image = qrCell.AddImage(qrSource);
            image.Width = Unit.FromCentimeter(3);
            image.Height = Unit.FromCentimeter(3);
            image.Interpolate = false;
        }

        var spacer = section.AddParagraph();
        spacer.Format.Font.Size = 1;
        spacer.Format.SpaceAfter = Unit.FromMillimeter(1);
    }

    private static void AddMeta(Section section, TaxSubject supplier, IssuedInvoice invoice)
    {
        var iban = supplier.Iban.NullOrTrim() ?? CzechIban.TryFromAccount(supplier.BankAccount);
        var heading = section.AddParagraph();
        heading.Format.SpaceAfter = Unit.FromMillimeter(1);
        AddLabel(heading, "PLATEBNÍ PODMÍNKY");

        var table = section.AddTable();
        table.AddColumn(Unit.FromCentimeter(8.5));
        table.AddColumn(Unit.FromCentimeter(8.5));
        var row = table.AddRow();

        var left = row.Cells[0].AddParagraph();
        SetBodyLineSpacing(left);
        if (!string.IsNullOrWhiteSpace(supplier.BankAccount)) AddLine(left, $"Bankovní účet: {supplier.BankAccount}");
        if (iban is not null) AddLine(left, $"IBAN: {iban}");
        AddLine(left, $"Variabilní symbol: {invoice.PaymentVariableSymbol}", bold: true);
        if (!string.IsNullOrWhiteSpace(invoice.PaymentMethod)) AddLine(left, $"Způsob platby: {invoice.PaymentMethod}");

        var right = row.Cells[1].AddParagraph();
        SetBodyLineSpacing(right);
        right.Format.LeftIndent = Unit.FromCentimeter(0.5);
        AddLine(right, $"Datum vystavení: {FormatDate(invoice.IssueDate)}");
        AddLine(right, $"Datum zdan. plnění (DUZP): {FormatDate(invoice.TaxableSupplyDate)}");
        AddLine(right, $"Datum splatnosti: {FormatDate(invoice.DueDate)}", bold: true);

        section.AddParagraph().Format.SpaceAfter = Unit.FromMillimeter(4);
    }

    private static void AddItemsTable(Section section, IssuedInvoice invoice)
    {
        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = Colors.LightGray;
        table.Format.Font.Size = 9;
        table.AddColumn(Unit.FromCentimeter(6.05)); // popis
        table.AddColumn(Unit.FromCentimeter(1.35)); // počet
        table.AddColumn(Unit.FromCentimeter(0.9)); // MJ
        table.AddColumn(Unit.FromCentimeter(2.25)); // cena/MJ
        table.AddColumn(Unit.FromCentimeter(1.35)); // sazba
        table.AddColumn(Unit.FromCentimeter(2.7)); // základ
        table.AddColumn(Unit.FromCentimeter(2.4)); // DPH

        var header = table.AddRow();
        SetRowPadding(header);
        header.Shading.Color = Colors.WhiteSmoke;
        header.Format.Font.Bold = true;
        SetCell(header.Cells[0], "Popis", ParagraphAlignment.Left);
        SetCell(header.Cells[1], "Počet", ParagraphAlignment.Right);
        SetCell(header.Cells[2], "MJ", ParagraphAlignment.Left);
        SetCell(header.Cells[3], "Cena/MJ", ParagraphAlignment.Center);
        SetCell(header.Cells[4], "Sazba", ParagraphAlignment.Center);
        SetCell(header.Cells[5], "Základ", ParagraphAlignment.Right);
        SetCell(header.Cells[6], "DPH", ParagraphAlignment.Right);

        foreach (var item in invoice.Items)
        {
            var row = table.AddRow();
            SetRowPadding(row);
            SetCell(row.Cells[0], item.Description, ParagraphAlignment.Left);
            SetCell(row.Cells[1], FormatQuantity(item.Quantity), ParagraphAlignment.Right);
            SetCell(row.Cells[2], item.Unit, ParagraphAlignment.Left);
            SetCell(row.Cells[3], FormatMoney(item.UnitPriceCzk), ParagraphAlignment.Right);
            SetCell(row.Cells[4], FormatRate(item.VatRate), ParagraphAlignment.Right);
            SetCell(row.Cells[5], FormatMoney(item.LineBaseCzk), ParagraphAlignment.Right);
            SetCell(row.Cells[6], FormatMoney(item.LineVatCzk), ParagraphAlignment.Right);
        }

        section.AddParagraph().Format.SpaceAfter = Unit.FromMillimeter(3);
    }

    private static void AddRecap(Section section, IssuedInvoice invoice)
    {
        var currency = CurrencyLabel(invoice.Currency);

        // Rekapitulace DPH po sazbách + součty základu a daně.
        var recap = section.AddParagraph();
        SetBodyLineSpacing(recap);
        AddLabel(recap, "REKAPITULACE DPH");
        foreach (var x in invoice.VatRecap())
        {
            AddLine(recap, $"Sazba {FormatRate(x.Rate)}: základ {FormatMoney(x.BaseCzk)}, DPH {FormatMoney(x.VatCzk)} {currency}");
        }

        AddLine(recap, $"Základ celkem: {FormatMoney(invoice.TotalBaseCzk)} {currency}");
        AddLine(recap, $"DPH celkem: {FormatMoney(invoice.TotalVatCzk)} {currency}");

        // "Celkem k úhradě" přes celou šířku stránky, ať měna nepřetéká na další řádek.
        var total = section.AddParagraph();
        total.Format.SpaceBefore = Unit.FromMillimeter(3);
        total.Format.Borders.Top = new Border { Width = 0.75, Color = Colors.Gray };
        total.Format.Borders.Bottom = new Border { Width = 0.75, Color = Colors.Gray };
        total.Format.Borders.Distance = Unit.FromMillimeter(2);
        var totalText = total.AddFormattedText($"Celkem k úhradě: {FormatMoney(invoice.TotalGrossCzk)} {currency}");
        totalText.Bold = true;
        totalText.Size = 14;
    }

    private static void AddFooter(Section section, IssuedInvoice invoice)
    {
        if (!string.IsNullOrWhiteSpace(invoice.Note))
        {
            var note = section.AddParagraph(invoice.Note);
            SetBodyLineSpacing(note);
            note.Format.SpaceBefore = Unit.FromMillimeter(6);
        }

        if (!string.IsNullOrWhiteSpace(invoice.Footer))
        {
            var footer = section.AddParagraph(invoice.Footer);
            SetBodyLineSpacing(footer);
            footer.Format.Font.Size = 8;
            footer.Format.Font.Color = Colors.Gray;
            footer.Format.SpaceBefore = Unit.FromMillimeter(6);
        }
    }

    // Měna pro tisk: CZK zobrazujeme jako "Kč".
    private static string CurrencyLabel(string currency)
        => string.Equals(currency, "CZK", StringComparison.OrdinalIgnoreCase) ? "Kč" : currency;

    private static void AddLabel(Paragraph paragraph, string text)
    {
        var label = paragraph.AddFormattedText(text + "\n");
        label.Bold = true;
        label.Size = 8;
        label.Color = Colors.Gray;
    }

    private static void AddLine(Paragraph paragraph, string? text, bool bold = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var run = paragraph.AddFormattedText(text + "\n");
        run.Bold = bold;
    }

    private static void SetBodyLineSpacing(Paragraph paragraph)
    {
        paragraph.Format.LineSpacingRule = LineSpacingRule.AtLeast;
        paragraph.Format.LineSpacing = Unit.FromPoint(12);
    }

    private static void SetCell(Cell cell, string text, ParagraphAlignment alignment)
    {
        var paragraph = cell.AddParagraph(text ?? "");
        paragraph.Format.Alignment = alignment;
        // Vnitřní odsazení textu od svislých linek (MigraDoc nemá padding buňky).
        var padding = Unit.FromMillimeter(0.8);
        paragraph.Format.LeftIndent = padding;
        paragraph.Format.RightIndent = padding;
        cell.VerticalAlignment = VerticalAlignment.Center;
    }

    private static void SetRowPadding(Row row)
    {
        row.TopPadding = Unit.FromMillimeter(1.1);
        row.BottomPadding = Unit.FromMillimeter(1.1);
    }

    private static string SupplierName(TaxSubject supplier)
    {
        var personName = string.Join(" ", new[] { supplier.FirstName, supplier.LastName }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        return supplier.DisplayName.NullOrTrim()
            ?? personName.NullOrTrim()
            ?? supplier.Dic.NullOrTrim()
            ?? "(dodavatel)";
    }

    private static string FormatDate(DateOnly date) => date.ToString("d.M.yyyy", Czech);
    private static string FormatMoney(decimal value) => value.ToString("#,##0.00", Czech);
    private static string FormatQuantity(decimal value) => value.ToString("0.###", Czech);

    private static string FormatRate(VatRateKind rate) => rate switch
    {
        VatRateKind.Reduced12 => "12%",
        VatRateKind.Zero0 => "0%",
        _ => "21%"
    };

    private static string? JoinAddress(string? street, string? houseNumber)
        => string.Join(" ", new[] { street, houseNumber }.Where(x => !string.IsNullOrWhiteSpace(x))).NullOrTrim();

    private static string? JoinCity(string? postalCode, string? city)
        => string.Join(" ", new[] { postalCode, city }.Where(x => !string.IsNullOrWhiteSpace(x))).NullOrTrim();
}

internal static class InvoiceStringExtensions
{
    public static string? NullOrTrim(this string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
