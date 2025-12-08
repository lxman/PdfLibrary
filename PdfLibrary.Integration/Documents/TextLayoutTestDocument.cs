using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;

namespace PdfLibrary.Integration.Documents;

/// <summary>
/// Tests text layout features: character/word spacing, horizontal scaling, text rise, leading
/// </summary>
public class TextLayoutTestDocument : ITestDocument
{
    public string Name => "TextLayout";
    public string Description => "Tests character spacing, word spacing, horizontal scaling, text rise, and leading";

    public void Generate(string outputPath)
    {
        PdfDocumentBuilder doc = new PdfDocumentBuilder()
            .WithMetadata(m => m.SetTitle("Text Layout Tests").SetAuthor("PdfLibrary.Integration"));

        doc.AddPage(PdfPageSize.Letter, page =>
        {
            double y = 750;
            const double leftMargin = 50;

            // Title
            page.AddText("Text Layout Tests", leftMargin, y, "Helvetica-Bold", 18);
            y -= 45;

            // === Character Spacing Section (Tc operator) ===
            page.AddText("Character Spacing (Tc operator)", leftMargin, y, "Helvetica-Bold", 14);
            y -= 25;

            double[] charSpacings = new[] { 0.0, 1.0, 2.0, 4.0, -0.5 };
            foreach (double spacing in charSpacings)
            {
                string label = spacing >= 0 ? $"+{spacing:F1}pt" : $"{spacing:F1}pt";
                page.AddText($"SPACING ({label}): HELLO WORLD", leftMargin, y)
                    .Font("Helvetica")
                    .CharacterSpacing(spacing);
                y -= 18;
            }

            y -= 15;

            // === Word Spacing Section (Tw operator) ===
            page.AddText("Word Spacing (Tw operator)", leftMargin, y, "Helvetica-Bold", 14);
            y -= 25;

            double[] wordSpacings = new[] { 0.0, 5.0, 10.0, 20.0, -2.0 };
            foreach (double spacing in wordSpacings)
            {
                string label = spacing >= 0 ? $"+{spacing:F0}pt" : $"{spacing:F0}pt";
                page.AddText($"Word spacing ({label}): The quick brown fox jumps", leftMargin, y)
                    .Font("Helvetica", 11)
                    .WordSpacing(spacing);
                y -= 18;
            }

            y -= 15;

            // === Horizontal Scaling Section (Tz operator) ===
            page.AddText("Horizontal Scaling (Tz operator)", leftMargin, y, "Helvetica-Bold", 14);
            y -= 25;

            var scales = new[] { 50.0, 75.0, 100.0, 125.0, 150.0 };
            foreach (double scale in scales)
            {
                page.AddText($"Scale {scale:F0}%: Horizontal Text", leftMargin, y)
                    .Font("Helvetica")
                    .HorizontalScale(scale);
                y -= 18;
            }

            y -= 5;

            // Convenience methods
            page.AddText("Condensed (80%):", leftMargin, y, "Helvetica", 10);
            page.AddText("Condensed text example", leftMargin + 110, y)
                .Font("Helvetica")
                .Condensed();
            y -= 18;

            page.AddText("Expanded (120%):", leftMargin, y, "Helvetica", 10);
            page.AddText("Expanded text", leftMargin + 110, y)
                .Font("Helvetica")
                .Expanded();
            y -= 25;

            // === Text Rise Section (Ts operator) ===
            page.AddText("Text Rise (Ts operator) - Superscript/Subscript", leftMargin, y, "Helvetica-Bold", 14);
            y -= 30;

            // Manual rise values
            page.AddText("Manual rise: ", leftMargin, y, "Helvetica", 12);
            double riseX = leftMargin + 90;

            page.AddText("baseline", riseX, y)
                .Font("Helvetica")
                .Color(PdfColor.Black);
            riseX += 55;

            page.AddText("+4pt", riseX, y)
                .Font("Helvetica", 10)
                .Rise(4)
                .Color(PdfColor.Blue);
            riseX += 35;

            page.AddText("+8pt", riseX, y)
                .Font("Helvetica", 10)
                .Rise(8)
                .Color(PdfColor.Blue);
            riseX += 35;

            page.AddText("-3pt", riseX, y)
                .Font("Helvetica", 10)
                .Rise(-3)
                .Color(PdfColor.Red);
            riseX += 35;

            page.AddText("-6pt", riseX, y)
                .Font("Helvetica", 10)
                .Rise(-6)
                .Color(PdfColor.Red);

            y -= 25;

            // Superscript example: E = mc²
            double superX = leftMargin;
            page.AddText("Superscript: E = mc", superX, y, "Helvetica", 14);
            superX += PdfPageBuilder.MeasureText("Superscript: E = mc", "Helvetica", 14);
            page.AddText("2", superX, y)
                .Font("Helvetica", 10)
                .Superscript()
                .Color(PdfColor.Blue);
            superX += PdfPageBuilder.MeasureText("2", "Helvetica", 7); // 7pt after Superscript() reduces size

            superX += 10; // small gap
            page.AddText("and x", superX, y, "Helvetica", 14);
            superX += PdfPageBuilder.MeasureText("and x", "Helvetica", 14);
            page.AddText("n+1", superX, y)
                .Font("Helvetica", 9)
                .Superscript()
                .Color(PdfColor.Blue);

            y -= 22;

            // Subscript example: H₂O and CO₂
            double subX = leftMargin;
            page.AddText("Subscript: H", subX, y, "Helvetica", 14);
            subX += PdfPageBuilder.MeasureText("Subscript: H", "Helvetica", 14);
            page.AddText("2", subX, y)
                .Font("Helvetica", 10)
                .Subscript()
                .Color(PdfColor.Red);
            subX += PdfPageBuilder.MeasureText("2", "Helvetica", 7); // 7pt after Subscript() reduces size

            page.AddText("O and CO", subX, y, "Helvetica", 14);
            subX += PdfPageBuilder.MeasureText("O and CO", "Helvetica", 14);
            page.AddText("2", subX, y)
                .Font("Helvetica", 10)
                .Subscript()
                .Color(PdfColor.Red);

            y -= 30;

            // === Combined Spacing Effects ===
            page.AddText("Combined Spacing Effects", leftMargin, y, "Helvetica-Bold", 14);
            y -= 25;

            // Normal
            page.AddText("Normal: The quick brown fox jumps over the lazy dog", leftMargin, y, "Helvetica", 11);
            y -= 18;

            // Tight (negative char spacing, less word spacing)
            page.AddText("Tight: The quick brown fox jumps over the lazy dog", leftMargin, y)
                .Font("Helvetica", 11)
                .CharacterSpacing(-0.3)
                .WordSpacing(-1);
            y -= 18;

            // Loose (positive char spacing, more word spacing)
            page.AddText("Loose: The quick brown fox jumps", leftMargin, y)
                .Font("Helvetica", 11)
                .CharacterSpacing(1)
                .WordSpacing(5);
            y -= 18;

            // Very expanded
            page.AddText("Spread: WIDE TEXT", leftMargin, y)
                .Font("Helvetica-Bold", 11)
                .CharacterSpacing(3)
                .HorizontalScale(130);
            y -= 30;

            // === Text Matrix Scaling Demo ===
            page.AddText("Font Size via Text Matrix", leftMargin, y, "Helvetica-Bold", 14);
            y -= 20;
            page.AddText("(Tests Tf=1 with scaling in Tm matrix)", leftMargin, y, "Helvetica", 10);
            y -= 25;

            // These all render at different effective sizes
            // even though they could use Tf=1 with matrix scaling
            page.AddText("12pt text", leftMargin, y, "Helvetica", 12);
            page.AddText("24pt text", leftMargin + 80, y, "Helvetica", 24);
            page.AddText("8pt", leftMargin + 200, y, "Helvetica", 8);

            y -= 35;

            // === Alignment Demo ===
            page.AddText("Text Positioning Demo", leftMargin, y, "Helvetica-Bold", 14);
            y -= 25;

            double boxX = leftMargin;
            double boxWidth = 200;

            // Draw reference box
            page.AddPath()
                .Rectangle(boxX, y - 60, boxWidth, 55)
                .Stroke(PdfColor.FromGray(0.7));

            // Left-positioned text
            page.AddText("Left edge", boxX, y - 15, "Helvetica", 11);

            // Right-positioned text (manually calculated)
            var rightText = "Right edge";
            double textWidth = PdfPageBuilder.MeasureText(rightText, "Helvetica", 11);
            page.AddText(rightText, boxX + boxWidth - textWidth, y - 35, "Helvetica", 11);

            // Center-positioned text
            var centerText = "Centered";
            textWidth = PdfPageBuilder.MeasureText(centerText, "Helvetica", 11);
            page.AddText(centerText, boxX + (boxWidth - textWidth) / 2, y - 55, "Helvetica", 11);

            page.AddText("Manual positioning using MeasureText()", boxX + boxWidth + 20, y - 35, "Helvetica", 9);
        });

        doc.Save(outputPath);
    }
}
