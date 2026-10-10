using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Morph.Avalonia.Demo;

public static class SampleDocument
{
    public static string CreateAddresses(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "sample-addresses.html");
        File.WriteAllText(path, """
            <!doctype html><html><body>
            <h1>PROPERTY ADDRESS EVIDENCE</h1>
            <p>Synthetic example: one reviewed property, October 2026.</p>
            <p>Property address: 42 Example St, Edinburgh, EH11AA</p>
            <p>Subject property: 42 Example Street, Edinburgh, EH1 1AA</p>
            <h2>Property details - separate boxes</h2>
            <p>Street: 42 Example Street</p>
            <p>Town: Edinburgh</p>
            <p>Postcode: EH1 1AA</p>
            <h2>Correspondence</h2>
            <p>Correspondence address: 42 Example Street, Edinburgh, EH1 1AA</p>
            <h2>Competing unit observations</h2>
            <p>Property unit two: Flat 2, 42 Example Street, Edinburgh, EH1 1AA</p>
            <p>Property unit three: Flat 3, 42 Example Street, Edinburgh, EH1 1AA</p>
            <p>Equivalent mentions coalesce; distinct roles and incompatible units remain visible.</p>
            </body></html>
            """);
        return path;
    }

    public static string Create(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "sample-application.docx");
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        var body = new Body();
        main.Document = new Document(body);

        void Paragraph(string text, bool heading = false)
        {
            var properties = new RunProperties(new RunFonts { Ascii = "Arial", HighAnsi = "Arial" },
                new FontSize { Val = heading ? "36" : "24" });
            if (heading) properties.Append(new Bold());
            body.Append(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "180" }),
                new Run(properties, new Text(text) { Space = SpaceProcessingModeValues.Preserve })));
        }

        Paragraph("APPLICATION RECORD", true);
        Paragraph("Synthetic document • Morph Avalonia demonstration");
        Paragraph("Applicant: Alex Morgan");
        Paragraph("Reference: APP-2026-0042");
        Paragraph("Date of birth: 14 February 1988");
        Paragraph("Requested amount: GBP 125,000.00");
        Paragraph("Property: 42 Example Street, Edinburgh");
        Paragraph("Evidence remains connected to the exact page and region it came from.");
        Paragraph("Review the applicant and requested amount in the evidence panel.");

        var table = new Table(new TableProperties(new TableBorders(
            new TopBorder { Val = BorderValues.Single, Size = 4 },
            new BottomBorder { Val = BorderValues.Single, Size = 4 },
            new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 })));
        foreach (var cells in new[] { new[] { "Item", "Amount" }, new[] { "Deposit", "GBP 25,000.00" }, new[] { "Requested finance", "GBP 125,000.00" } })
        {
            var row = new TableRow();
            foreach (var cell in cells)
                row.Append(new TableCell(new TableCellProperties(new TableCellWidth { Width = "4200", Type = TableWidthUnitValues.Dxa }),
                    new Paragraph(new Run(new Text(cell)))));
            table.Append(row);
        }
        body.Append(table);
        body.Append(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
        Paragraph("SUPPORTING STATEMENT", true);
        Paragraph("Applicant: Alex Morgan");
        Paragraph("Statement date: 30 September 2026");
        Paragraph("Available funds: GBP 31,450.75");
        Paragraph("This second page exercises navigation and search across pages.");
        Paragraph("These records are invented and contain no customer data.");
        body.Append(new SectionProperties(new PageSize { Width = 11906, Height = 16838 },
            new PageMargin { Top = 1100, Bottom = 1100, Left = 1100, Right = 1100 }));
        main.Document.Save();
        return path;
    }

    public static string CreateHtml(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "sample-page.html");
        File.WriteAllText(path, """
            <!doctype html><html><body>
            <h1>Application summary</h1><p>Applicant: Alex Morgan</p>
            <p>Requested amount: GBP 125,000.00</p>
            <h2>Evidence from a web page</h2>
            <p>HTML can enter the same document review workflow after StyloExtract identifies the content areas.</p>
            <table><tr><th>Field</th><th>Value</th></tr><tr><td>Reference</td><td>APP-2026-0042</td></tr></table>
            </body></html>
            """);
        return path;
    }
}
