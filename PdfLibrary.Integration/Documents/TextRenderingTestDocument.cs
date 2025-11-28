using PdfLibrary.Builder;

namespace PdfLibrary.Integration.Documents;

/// <summary>
/// Tests text rendering modes: fill, stroke, fill+stroke, invisible, clipping
/// </summary>
public class TextRenderingTestDocument : ITestDocument
{
    public string Name => "TextRendering";
    public string Description => "Tests text rendering modes (Tr operator): fill, stroke, fill+stroke, invisible, clip";

    public void Generate(string outputPath)
    {
        PdfDocumentBuilder doc = new PdfDocumentBuilder()
            .WithMetadata(m => m.SetTitle("Text Rendering Mode Tests").SetAuthor("PdfLibrary.Integration"));

        doc.AddPage(PdfPageSize.Letter, page =>
        {
            double y = 750;
            const double leftMargin = 50;

            // Title
            page.AddText("Text Rendering Mode Tests", leftMargin, y, "Helvetica-Bold", 16);
            y -= 35;

            // === Fill Mode (Tr=0, default) ===
            page.AddText("Mode 0: Fill (default)", leftMargin, y, "Helvetica-Bold", 11);
            y -= 25;

            page.AddText("FILLED TEXT", leftMargin, y)
                .Font("Helvetica-Bold", 28)
                .RenderMode(PdfTextRenderMode.Fill)
                .Color(PdfColor.Blue);
            y -= 38;

            // === Stroke Mode (Tr=1) ===
            page.AddText("Mode 1: Stroke (outline only)", leftMargin, y, "Helvetica-Bold", 11);
            y -= 25;

            page.AddText("OUTLINE", leftMargin, y)
                .Font("Helvetica-Bold", 28)
                .RenderMode(PdfTextRenderMode.Stroke)
                .StrokeColor(PdfColor.Red);

            // Using convenience method (on same line)
            page.AddText("OUTLINE ALT", leftMargin + 200, y)
                .Font("Helvetica-Bold", 28)
                .Outline(1.5)
                .StrokeColor(PdfColor.Green, 1.5);
            y -= 38;

            // === Fill + Stroke Mode (Tr=2) ===
            page.AddText("Mode 2: Fill then Stroke", leftMargin, y, "Helvetica-Bold", 11);
            y -= 25;

            page.AddText("FILLED+OUTLINE", leftMargin, y)
                .Font("Helvetica-Bold", 28)
                .RenderMode(PdfTextRenderMode.FillStroke)
                .Color(PdfColor.Yellow)
                .StrokeColor(PdfColor.Black);
            y -= 38;

            // Using convenience method
            page.AddText("CONVENIENCE", leftMargin, y)
                .Font("Helvetica-Bold", 28)
                .FillAndOutline(PdfColor.FromCmyk(0, 0.8, 0.8, 0), 1.5)
                .Color(PdfColor.FromCmyk(0.8, 0, 0.8, 0));
            y -= 40;

            // === Invisible Mode (Tr=3) ===
            page.AddText("Mode 3: Invisible (for searchable OCR)", leftMargin, y, "Helvetica-Bold", 11);
            y -= 20;

            // Draw a box where invisible text would be
            page.AddPath()
                .Rectangle(leftMargin, y - 25, 200, 22)
                .Stroke(PdfColor.FromGray(0.8));

            page.AddText("[Invisible text here]", leftMargin + 5, y - 18)
                .Font("Helvetica", 10)
                .Invisible();

            page.AddText("(select to see)", leftMargin + 210, y - 18, "Helvetica", 9);
            y -= 40;

            // === Stroke Width Comparison ===
            page.AddText("Stroke Width Comparison", leftMargin, y, "Helvetica-Bold", 11);
            y -= 25;

            var strokeWidths = new[] { 0.25, 0.5, 1.0, 2.0, 3.0 };
            double x = leftMargin;

            foreach (double sw in strokeWidths)
            {
                page.AddText("Aa", x, y)
                    .Font("Helvetica-Bold", 24)
                    .Outline(sw)
                    .StrokeColor(PdfColor.Black, sw);

                page.AddText($"{sw}pt", x + 5, y - 22, "Helvetica", 7);
                x += 55;
            }
            y -= 45;

            // === Color Combinations ===
            page.AddText("Fill + Stroke Color Combinations", leftMargin, y, "Helvetica-Bold", 11);
            y -= 28;

            (PdfColor, PdfColor, string)[] colorCombos = new[]
            {
                (PdfColor.White, PdfColor.Black, "Wht/Blk"),
                (PdfColor.Red, PdfColor.Black, "Red/Blk"),
                (PdfColor.Yellow, PdfColor.Blue, "Yel/Blu"),
                (PdfColor.FromGray(0.9), PdfColor.Red, "Gry/Red"),
            };

            x = leftMargin;
            foreach ((PdfColor fill, PdfColor stroke, string name) in colorCombos)
            {
                // Draw background for visibility
                page.AddPath()
                    .Rectangle(x - 2, y - 22, 75, 28)
                    .Fill(PdfColor.FromGray(0.95));

                page.AddText("TEXT", x, y)
                    .Font("Helvetica-Bold", 20)
                    .FillAndOutline(stroke)
                    .Color(fill);

                page.AddText(name, x + 3, y - 20, "Helvetica", 6);
                x += 85;
            }
            y -= 45;

            // === Large Outlined Text ===
            page.AddText("Large Outlined Text", leftMargin, y, "Helvetica-Bold", 11);
            y -= 40;

            page.AddText("BIG", leftMargin, y)
                .Font("Helvetica-Bold", 56)
                .FillAndOutline(PdfColor.FromCmyk(1, 0, 0, 0), 2)
                .Color(PdfColor.White);

            page.AddText("PDF", leftMargin + 130, y)
                .Font("Helvetica-Bold", 56)
                .Outline(2.5)
                .StrokeColor(PdfColor.FromCmyk(0, 1, 0, 0), 2.5);

            y -= 65;

            // === Rendering Mode Reference ===
            page.AddText("Rendering Mode Reference (Tr values)", leftMargin, y, "Helvetica-Bold", 10);
            y -= 14;

            var modes = new[]
            {
                "0=Fill  1=Stroke  2=Fill+Stroke  3=Invisible",
                "4=Fill+Clip  5=Stroke+Clip  6=Fill+Stroke+Clip  7=Clip"
            };

            foreach (string mode in modes)
            {
                page.AddText(mode, leftMargin, y, "Courier", 8);
                y -= 11;
            }
        });

        doc.Save(outputPath);
    }
}
