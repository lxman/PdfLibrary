using PdfLibrary.Builder;

namespace PdfLibrary.Integration.Documents;

/// <summary>
/// Tests basic text features: standard fonts, sizes, colors, positioning
/// </summary>
public class TextBasicsTestDocument : ITestDocument
{
    public string Name => "TextBasics";
    public string Description => "Tests standard PDF fonts, font sizes, text colors, and positioning";

    public void Generate(string outputPath)
    {
        PdfDocumentBuilder doc = new PdfDocumentBuilder()
            .WithMetadata(m => m.SetTitle("Text Basics Tests").SetAuthor("PdfLibrary.Integration"));

        doc.AddPage(PdfPageSize.Letter, page =>
        {
            double y = 750;
            const double leftMargin = 50;

            // Title
            page.AddText("Text Basics Tests", leftMargin, y, "Helvetica-Bold", 18);
            y -= 30;

            // === Standard 14 PDF Fonts Section ===
            page.AddText("Standard PDF Fonts (Base 14)", leftMargin, y, "Helvetica-Bold", 12);
            y -= 18;

            var standardFonts = new[]
            {
                ("Helvetica", "Helvetica - The quick brown fox jumps over the lazy dog"),
                ("Helvetica-Bold", "Helvetica-Bold - The quick brown fox jumps"),
                ("Helvetica-Oblique", "Helvetica-Oblique - The quick brown fox jumps"),
                ("Helvetica-BoldOblique", "Helvetica-BoldOblique - The quick brown fox"),
                ("Times-Roman", "Times-Roman - The quick brown fox jumps over the lazy dog"),
                ("Times-Bold", "Times-Bold - The quick brown fox jumps over"),
                ("Times-Italic", "Times-Italic - The quick brown fox jumps over the lazy"),
                ("Times-BoldItalic", "Times-BoldItalic - The quick brown fox jumps"),
                ("Courier", "Courier - The quick brown fox jumps over"),
                ("Courier-Bold", "Courier-Bold - The quick brown fox jumps"),
                ("Courier-Oblique", "Courier-Oblique - The quick brown fox"),
                ("Courier-BoldOblique", "Courier-BoldOblique - Quick brown fox"),
            };

            foreach ((string fontName, string text) in standardFonts)
            {
                page.AddText(text, leftMargin, y, fontName, 9);
                y -= 13;
            }

            y -= 10;

            // === Font Sizes Section ===
            page.AddText("Font Sizes (Helvetica)", leftMargin, y, "Helvetica-Bold", 12);
            y -= 18;

            var sizes = new[] { 8, 10, 12, 14, 18, 24, 30 };
            foreach (int size in sizes)
            {
                page.AddText($"{size}pt Sample", leftMargin, y, "Helvetica", size);
                y -= size + 4;
            }

            y -= 8;

            // === Text Colors Section ===
            page.AddText("Text Colors", leftMargin, y, "Helvetica-Bold", 12);
            y -= 20;

            // RGB Colors
            page.AddText("RGB Colors:", leftMargin, y, "Helvetica", 9);
            y -= 14;

            double x = leftMargin;
            (PdfColor, string)[] rgbColors = new[]
            {
                (PdfColor.Red, "Red"),
                (PdfColor.Green, "Green"),
                (PdfColor.Blue, "Blue"),
                (PdfColor.FromRgb(255, 165, 0), "Orange"),
                (PdfColor.FromRgb(128, 0, 128), "Purple"),
            };

            foreach ((PdfColor color, string name) in rgbColors)
            {
                page.AddText(name, x, y)
                    .Font("Helvetica-Bold", 11)
                    .Color(color);
                x += 60;
            }

            y -= 20;

            // CMYK Colors
            page.AddText("CMYK Colors:", leftMargin, y, "Helvetica", 9);
            y -= 14;

            x = leftMargin;
            (PdfColor, string)[] cmykColors = new[]
            {
                (PdfColor.CmykCyan, "Cyan"),
                (PdfColor.CmykMagenta, "Magenta"),
                (PdfColor.CmykYellow, "Yellow"),
                (PdfColor.FromCmyk(0, 0, 0, 1), "Black"),
                (PdfColor.FromCmyk(0.5, 0.5, 0, 0), "Violet"),
            };

            foreach ((PdfColor color, string name) in cmykColors)
            {
                page.AddText(name, x, y)
                    .Font("Helvetica-Bold", 11)
                    .Color(color);
                x += 70;
            }

            y -= 20;

            // Grayscale
            page.AddText("Grayscale:", leftMargin, y, "Helvetica", 9);
            y -= 14;

            x = leftMargin;
            for (double gray = 0; gray <= 1.0; gray += 0.2)
            {
                var label = $"{gray:F1}";
                page.AddText(label, x, y)
                    .Font("Helvetica-Bold", 11)
                    .Color(PdfColor.FromGray(gray));
                x += 45;
            }

            y -= 20;

            // === Rotated Text Section ===
            page.AddText("Rotated Text", leftMargin, y, "Helvetica-Bold", 12);
            y -= 40;

            // Draw baseline reference
            page.AddPath()
                .MoveTo(leftMargin, y)
                .LineTo(leftMargin + 350, y)
                .Stroke(PdfColor.FromGray(0.8), 0.5);

            x = leftMargin + 20;
            var rotations = new[] { 0, 15, 30, 45, 60, 90 };
            foreach (int angle in rotations)
            {
                page.AddText($"{angle}°", x, y)
                    .Font("Helvetica", 10)
                    .Rotate(angle)
                    .Color(PdfColor.Blue);
                x += 55;
            }

            y -= 40;

            // === Text Positioning Accuracy ===
            page.AddText("Text Positioning Accuracy", leftMargin, y, "Helvetica-Bold", 12);
            y -= 20;

            // Draw a grid to show positioning
            double gridX = leftMargin;
            double gridY = y - 50;
            double gridSize = 16;
            var gridCols = 10;
            var gridRows = 3;

            // Draw grid lines
            for (var row = 0; row <= gridRows; row++)
            {
                page.AddPath()
                    .MoveTo(gridX, gridY + row * gridSize)
                    .LineTo(gridX + gridCols * gridSize, gridY + row * gridSize)
                    .Stroke(PdfColor.FromGray(0.85), 0.5);
            }
            for (var col = 0; col <= gridCols; col++)
            {
                page.AddPath()
                    .MoveTo(gridX + col * gridSize, gridY)
                    .LineTo(gridX + col * gridSize, gridY + gridRows * gridSize)
                    .Stroke(PdfColor.FromGray(0.85), 0.5);
            }

            // Place letters at grid intersections
            var letters = "ABCDEFGHIJ";
            for (var i = 0; i < letters.Length; i++)
            {
                page.AddText(letters[i].ToString(), gridX + i * gridSize, gridY + 2 * gridSize)
                    .Font("Helvetica", 12)
                    .Color(PdfColor.Black);
            }

            page.AddText("Grid cell = 16pt, letters at intersections", gridX, gridY - 12, "Helvetica", 8);
        });

        doc.Save(outputPath);
    }
}
