using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Coffa.Api.Domain;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace Coffa.Api.Commercial;

public sealed class CoffaFontResolver : IFontResolver
{
    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new(bold ? "NotoSans-Bold" : "NotoSans-Regular", false, italic);
    public byte[] GetFont(string faceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Coffa.Api.Assets.Fonts.{faceName}.ttf") ?? throw new InvalidOperationException("The embedded PDF font is unavailable.");
        using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray();
    }
}

public sealed class CommercialPdf
{
    static CommercialPdf() { GlobalFontSettings.FontResolver ??= new CoffaFontResolver(); }
    private static readonly Color Ink = Color.FromRgb(27, 40, 33);
    private static readonly Color Green = Color.FromRgb(69, 101, 76);
    private static readonly Color Sage = Color.FromRgb(233, 238, 231);
    private static readonly Color Accent = Color.FromRgb(184, 77, 47);
    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");
    private sealed record Party(string Name, string Address, string Email, string ChamberOfCommerce, string VatNumber);
    private static Party Customer(Organization organization, string snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot)) { try { return JsonSerializer.Deserialize<Party>(snapshot, CommercialCalculations.Json) ?? new(organization.Name, organization.Address, organization.Email, organization.ChamberOfCommerce, organization.VatNumber); } catch (JsonException) { } }
        return new(organization.Name, organization.Address, organization.Email, organization.ChamberOfCommerce, organization.VatNumber);
    }
    private static BusinessSettings Issuer(BusinessSettings fallback, string snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot)) { try { return JsonSerializer.Deserialize<BusinessSettings>(snapshot, CommercialCalculations.Json) ?? fallback; } catch (JsonException) { } } return fallback;
    }
    public Task<byte[]> Quote(Quote quote, BusinessSettings issuer, Organization customer, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Render("Offerte", quote.Number, quote.Title, quote.IssuedAt.HasValue ? DateOnly.FromDateTime(quote.IssuedAt.Value.UtcDateTime) : CommercialService.Today, "Geldig tot", quote.ValidUntil, quote.Notes, quote.Terms, CommercialCalculations.Lines(quote.LinesJson), quote.NetAmount, quote.TaxAmount, quote.TotalAmount, Issuer(issuer, quote.IssuerSnapshot), Customer(customer, quote.CustomerSnapshot), quote.Status == "draft", false));
    }
    public Task<byte[]> Invoice(Invoice invoice, BusinessSettings issuer, Organization customer, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Render(invoice.Kind == "credit" ? "Creditfactuur" : "Factuur", invoice.Number, invoice.Title, invoice.InvoiceDate, "Vervaldatum", invoice.DueDate, invoice.Notes, invoice.Terms, CommercialCalculations.Lines(invoice.LinesJson), invoice.NetAmount, invoice.TaxAmount, invoice.TotalAmount, Issuer(issuer, invoice.IssuerSnapshot), Customer(customer, invoice.CustomerSnapshot), invoice.Status == "draft", invoice.Kind != "credit"));
    }
    private static byte[] Render(string type, string? number, string title, DateOnly date, string endLabel, DateOnly end, string notes, string terms, IReadOnlyList<CommercialLine> lines, decimal net, decimal tax, decimal total, BusinessSettings issuer, Party customer, bool draft, bool payment)
    {
        var doc = new Document(); doc.Info.Title = $"{type} {number ?? "concept"}"; doc.Info.Author = issuer.CompanyName; doc.Info.Subject = title;
        var normal = doc.Styles[StyleNames.Normal]!; normal.Font.Name = "Noto Sans"; normal.Font.Size = 9; normal.Font.Color = Ink; normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(5);
        doc.Styles[StyleNames.Heading1]!.Font.Size = 25; doc.Styles[StyleNames.Heading1]!.Font.Color = Ink; doc.Styles[StyleNames.Heading1]!.ParagraphFormat.SpaceAfter = Unit.FromPoint(9);
        var section = doc.AddSection(); section.PageSetup.PageFormat = PageFormat.A4; section.PageSetup.TopMargin = Unit.FromCentimeter(1.6); section.PageSetup.BottomMargin = Unit.FromCentimeter(2); section.PageSetup.LeftMargin = Unit.FromCentimeter(2); section.PageSetup.RightMargin = Unit.FromCentimeter(2);
        var brand = section.AddParagraph(); brand.Format.SpaceAfter = Unit.FromCentimeter(.8); var dc = brand.AddFormattedText("dc", TextFormat.Bold); dc.Font.Size = 38; dc.Font.Color = Green; var dot = brand.AddFormattedText(".", TextFormat.Bold); dot.Font.Size = 38; dot.Font.Color = Accent;
        var parties = section.AddTable(); parties.AddColumn(Unit.FromCentimeter(8.5)); parties.AddColumn(Unit.FromCentimeter(8.5)); var partyRow = parties.AddRow(); partyRow.TopPadding = 3; partyRow.BottomPadding = 3;
        partyRow[0].AddParagraph("AAN").Format.Font.Color = Green; partyRow[0].AddParagraph(customer.Name).Format.Font.Bold = true; partyRow[0].AddParagraph(customer.Address); if (!string.IsNullOrWhiteSpace(customer.Email)) partyRow[0].AddParagraph(customer.Email);
        if (!string.IsNullOrWhiteSpace(customer.VatNumber)) partyRow[0].AddParagraph("Btw: " + customer.VatNumber);
        partyRow[1].AddParagraph(issuer.CompanyName).Format.Font.Bold = true; partyRow[1].AddParagraph(issuer.Address); partyRow[1].AddParagraph(issuer.Email); partyRow[1].AddParagraph("KvK: " + issuer.ChamberOfCommerce); partyRow[1].AddParagraph("Btw: " + issuer.VatNumber);
        var spacer = section.AddParagraph(); spacer.Format.SpaceAfter = Unit.FromCentimeter(.5);
        var heading = section.AddParagraph(type + (draft ? " - CONCEPT" : ""), StyleNames.Heading1); heading.Format.KeepWithNext = true;
        var meta = section.AddParagraph(); meta.Format.SpaceAfter = Unit.FromPoint(15); meta.AddFormattedText(number ?? "Nog niet definitief genummerd", TextFormat.Bold); meta.AddText($"    |    {date:dd-MM-yyyy}    |    {endLabel}: {end:dd-MM-yyyy}");
        var subject = section.AddParagraph(title); subject.Format.Font.Size = 13; subject.Format.Font.Bold = true; subject.Format.SpaceAfter = Unit.FromPoint(14); subject.Format.KeepWithNext = true;
        var table = section.AddTable(); table.Rows.LeftIndent = Unit.Zero; table.Borders.Color = Color.FromRgb(217, 222, 212); table.Borders.Width = .4; table.TopPadding = Unit.FromPoint(7); table.BottomPadding = Unit.FromPoint(7); table.Format.Font.Size = 8;
        foreach (var width in new[] { 7.4, 1.2, 2.4, 1.4, 1.2, 3.4 }) table.AddColumn(Unit.FromCentimeter(width));
        for (var index = 1; index < 6; index++) table.Columns[index].Format.Alignment = ParagraphAlignment.Right;
        var header = table.AddRow(); header.HeadingFormat = true; header.Shading.Color = Green; header.Format.Font.Color = Colors.White; header.Format.Font.Bold = true;
        var labels = new[] { "Omschrijving", "Aantal", "Prijs", "Korting", "Btw", "Excl. btw" }; for (var index = 0; index < labels.Length; index++) header[index].AddParagraph(labels[index]);
        foreach (var line in lines)
        {
            var row = table.AddRow(); row[0].AddParagraph(line.Description); row[1].AddParagraph(line.Quantity.ToString("0.####", Dutch)); row[2].AddParagraph(line.UnitPrice.ToString("C", Dutch)); row[3].AddParagraph(line.DiscountPercent.ToString("0.##", Dutch) + "%"); row[4].AddParagraph(line.TaxRate.ToString("0.##", Dutch) + "%"); row[5].AddParagraph(line.NetAmount.ToString("C", Dutch));
        }
        var totals = section.AddTable(); totals.Rows.LeftIndent = Unit.Zero; totals.AddColumn(Unit.FromCentimeter(10)); totals.AddColumn(Unit.FromCentimeter(3.6)); totals.AddColumn(Unit.FromCentimeter(3.4)); totals.Columns[2].Format.Alignment = ParagraphAlignment.Right; totals.TopPadding = 6; totals.BottomPadding = 6;
        void TotalRow(string label, decimal value, bool strong = false) { var row = totals.AddRow(); row[1].AddParagraph(label); row[2].AddParagraph(value.ToString("C", Dutch)); if (strong) { row.Shading.Color = Sage; row.Format.Font.Bold = true; row.Format.Font.Size = 12; } }
        TotalRow("Subtotaal", net);
        foreach (var group in lines.GroupBy(x => x.TaxRate)) TotalRow($"Btw {group.Key:0.##}%", group.Sum(x => x.TaxAmount));
        if (lines.Count == 0) TotalRow("Btw", tax);
        TotalRow("Totaal", total, true);
        if (payment && !draft)
        {
            var text = section.AddParagraph(); text.Format.SpaceBefore = Unit.FromPoint(18); text.Format.SpaceAfter = Unit.FromPoint(12); text.Format.Font.Color = Green;
            text.AddText($"Graag vóór {end:dd-MM-yyyy} overmaken op {issuer.Iban}, onder vermelding van {number}.");
            if (!string.IsNullOrWhiteSpace(issuer.PaymentInstructions)) { text.AddLineBreak(); text.AddText(issuer.PaymentInstructions); }
        }
        if (!string.IsNullOrWhiteSpace(notes)) { var label = section.AddParagraph("Opmerkingen"); label.Format.Font.Bold = true; label.Format.SpaceBefore = 14; label.Format.KeepWithNext = true; section.AddParagraph(notes); }
        if (!string.IsNullOrWhiteSpace(terms)) { var label = section.AddParagraph("Voorwaarden"); label.Format.Font.Bold = true; label.Format.SpaceBefore = 14; label.Format.KeepWithNext = true; section.AddParagraph(terms); }
        if (draft) { var paragraph = section.AddParagraph("Concept - dit document is nog niet definitief uitgegeven."); paragraph.Format.Font.Color = Accent; paragraph.Format.SpaceBefore = 16; }
        var footer = section.Footers.Primary.AddParagraph(); footer.Format.Font.Size = 7; footer.Format.Font.Color = Green; footer.Format.Alignment = ParagraphAlignment.Center; footer.AddText($"{issuer.CompanyName}  |  {issuer.Email}  |  "); footer.AddPageField(); footer.AddText(" / "); footer.AddNumPagesField();
        var renderer = new PdfDocumentRenderer { Document = doc }; renderer.RenderDocument(); using var output = new MemoryStream(); renderer.PdfDocument.Save(output, false); return output.ToArray();
    }
}
