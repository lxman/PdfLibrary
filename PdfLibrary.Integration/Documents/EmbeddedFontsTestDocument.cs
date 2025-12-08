using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;

namespace PdfLibrary.Integration.Documents;

/// <summary>
/// Tests embedded TrueType font support
/// </summary>
public class EmbeddedFontsTestDocument : ITestDocument
{
    public string Name => "EmbeddedFonts";
    public string Description => "Tests TrueType font embedding with various font styles";

    // Font paths - these fonts should exist on the test system
    private const string FontDir = @"C:\Users\jorda\source\TestFonts";

    public void Generate(string outputPath)
    {
        PdfDocumentBuilder doc = new PdfDocumentBuilder()
            .WithMetadata(m => m.SetTitle("Embedded Fonts Tests").SetAuthor("PdfLibrary.Integration"));

        // Load embedded fonts
        string arialPath = Path.Combine(FontDir, "arial.ttf");
        string arialBoldPath = Path.Combine(FontDir, "arialbd.ttf");
        string arialItalicPath = Path.Combine(FontDir, "ariali.ttf");
        string algerPath = Path.Combine(FontDir, "ALGER.TTF");
        string agencyPath = Path.Combine(FontDir, "AGENCYR.TTF");
        string agencyBoldPath = Path.Combine(FontDir, "AGENCYB.TTF");

        // Only load fonts that exist
        if (File.Exists(arialPath))
            doc.LoadFont(arialPath, "Arial");
        if (File.Exists(arialBoldPath))
            doc.LoadFont(arialBoldPath, "Arial-Bold");
        if (File.Exists(arialItalicPath))
            doc.LoadFont(arialItalicPath, "Arial-Italic");
        if (File.Exists(algerPath))
            doc.LoadFont(algerPath, "Algerian");
        if (File.Exists(agencyPath))
            doc.LoadFont(agencyPath, "Agency");
        if (File.Exists(agencyBoldPath))
            doc.LoadFont(agencyBoldPath, "Agency-Bold");

        doc.AddPage(PdfPageSize.Letter, page =>
        {
            double y = 750;
            const double leftMargin = 50;

            // Title (using standard font)
            page.AddText("Embedded Fonts Tests", leftMargin, y, "Helvetica-Bold", 18);
            y -= 40;

            // === Embedded Arial Family ===
            page.AddText("Embedded Arial Family (TrueType)", leftMargin, y, "Helvetica-Bold", 14);
            y -= 25;

            if (File.Exists(arialPath))
            {
                page.AddText("Arial Regular: The quick brown fox jumps over the lazy dog", leftMargin, y, "Arial", 12);
                y -= 18;
            }

            if (File.Exists(arialBoldPath))
            {
                page.AddText("Arial Bold: The quick brown fox jumps over the lazy dog", leftMargin, y, "Arial-Bold", 12);
                y -= 18;
            }

            if (File.Exists(arialItalicPath))
            {
                page.AddText("Arial Italic: The quick brown fox jumps over the lazy dog", leftMargin, y, "Arial-Italic", 12);
                y -= 18;
            }

            y -= 15;

            // === Font Sizes with Embedded Font ===
            page.AddText("Embedded Font at Various Sizes", leftMargin, y, "Helvetica-Bold", 14);
            y -= 25;

            if (File.Exists(arialPath))
            {
                var sizes = new[] { 8, 10, 12, 14, 18, 24, 36 };
                foreach (int size in sizes)
                {
                    page.AddText($"{size}pt Arial", leftMargin, y, "Arial", size);
                    y -= size + 6;
                }
            }

            y -= 15;

            // === Decorative Fonts ===
            page.AddText("Decorative Embedded Fonts", leftMargin, y, "Helvetica-Bold", 14);
            y -= 30;

            if (File.Exists(algerPath))
            {
                page.AddText("ALGERIAN DECORATIVE FONT", leftMargin, y, "Algerian", 18);
                y -= 28;
                page.AddText("Algerian at 24pt", leftMargin, y, "Algerian", 24);
                y -= 35;
            }

            if (File.Exists(agencyPath))
            {
                page.AddText("Agency FB Regular - Modern Style", leftMargin, y, "Agency", 14);
                y -= 20;
            }

            if (File.Exists(agencyBoldPath))
            {
                page.AddText("Agency FB Bold - Modern Style", leftMargin, y, "Agency-Bold", 14);
                y -= 25;
            }

            y -= 10;

            // === Mixed Standard and Embedded ===
            page.AddText("Mixed Standard and Embedded Fonts", leftMargin, y, "Helvetica-Bold", 14);
            y -= 25;

            page.AddText("This line uses Helvetica (standard)", leftMargin, y, "Helvetica", 12);
            y -= 18;

            if (File.Exists(arialPath))
            {
                page.AddText("This line uses Arial (embedded)", leftMargin, y, "Arial", 12);
                y -= 18;
            }

            page.AddText("This line uses Times-Roman (standard)", leftMargin, y, "Times-Roman", 12);
            y -= 18;

            page.AddText("This line uses Courier (standard)", leftMargin, y, "Courier", 12);
            y -= 25;

            // === Text Styling with Embedded Fonts ===
            page.AddText("Styled Embedded Font Text", leftMargin, y, "Helvetica-Bold", 14);
            y -= 30;

            if (File.Exists(arialPath))
            {
                // Colored text
                page.AddText("Colored Arial Text", leftMargin, y)
                    .Font("Arial", 14)
                    .Color(PdfColor.Blue);
                y -= 22;

                // Rotated
                page.AddText("Rotated", leftMargin, y)
                    .Font("Arial")
                    .Rotate(15)
                    .Color(PdfColor.Red);
                y -= 25;

                // Character spacing
                page.AddText("S P A C E D  O U T", leftMargin, y)
                    .Font("Arial")
                    .CharacterSpacing(3);
                y -= 22;

                // Outlined
                page.AddText("OUTLINED", leftMargin, y)
                    .Font("Arial-Bold", 28)
                    .Outline(1.5)
                    .StrokeColor(PdfColor.FromCmyk(0, 0.8, 0, 0), 1.5);
                y -= 40;

                // Fill and stroke
                page.AddText("FILLED+STROKED", leftMargin, y)
                    .Font("Arial-Bold", 28)
                    .FillAndOutline(PdfColor.Black)
                    .Color(PdfColor.Yellow);
            }

            y -= 50;

            // === Character Coverage Test ===
            page.AddText("Character Coverage", leftMargin, y, "Helvetica-Bold", 14);
            y -= 25;

            if (!File.Exists(arialPath)) return;
            page.AddText("ABCDEFGHIJKLMNOPQRSTUVWXYZ", leftMargin, y, "Arial", 11);
            y -= 16;
            page.AddText("abcdefghijklmnopqrstuvwxyz", leftMargin, y, "Arial", 11);
            y -= 16;
            page.AddText("0123456789", leftMargin, y, "Arial", 11);
            y -= 16;
            page.AddText("!@#$%^&*()_+-=[]{}|;':\",./<>?", leftMargin, y, "Arial", 11);
        });

        doc.Save(outputPath);
    }
}
