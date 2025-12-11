using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;

namespace PdfLibrary.Integration.Documents;

/// <summary>
/// Tests Separation color space (spot colors) with varying tint values
/// </summary>
public class SeparationColorTestDocument : ITestDocument
{
    public string Name => "SeparationColors";
    public string Description => "Tests Separation color space with varying tints and multiple spot colors";

    public void Generate(string outputPath)
    {
        PdfDocumentBuilder doc = new PdfDocumentBuilder()
            .WithMetadata(m => m.SetTitle("Separation Color Space Tests").SetAuthor("PdfLibrary.Integration"));

        doc.AddPage(PdfPageSize.Letter, page =>
        {
            double y = 750;
            const double leftMargin = 50;

            // Title
            page.AddText("Separation Color Space Tests", leftMargin, y, "Helvetica-Bold", 18);
            y -= 40;

            // === Test 1: Single spot color with varying tints ===
            page.AddText("1. Orange Spot Color - Tint Variations", leftMargin, y, "Helvetica-Bold", 12);
            y -= 30;

            // Create a series of rectangles with different tints (0.0 to 1.0)
            double x = leftMargin;
            for (var i = 0; i <= 10; i++)
            {
                double tint = i / 10.0;
                PdfColor orangeColor = PdfColor.FromSeparation("Orange", tint);
                page.AddRectangle(PdfRect.FromPoints(x, y - 40, 40, 40), orangeColor, PdfColor.Black, 0.5);
                page.AddText($"{tint:F1}", x + 10, y - 55, "Helvetica", 8);
                x += 50;
            }
            y -= 80;

            page.AddText("Tint: 0.0 = White (no ink), 1.0 = Full spot color", leftMargin, y, "Helvetica", 9);
            y -= 40;

            // === Test 2: Multiple spot colors (simulating PMS colors) ===
            page.AddText("2. Multiple Spot Colors (PMS Simulation)", leftMargin, y, "Helvetica-Bold", 12);
            y -= 30;

            var spotColors = new (string name, double tint)[]
            {
                ("PMS485", 1.0),      // Red-ish
                ("PMS300", 1.0),      // Blue-ish
                ("PMS375", 1.0),      // Green-ish
                ("RefxBlue", 0.8),    // Custom spot color
                ("BrandOrange", 0.6),  // Custom spot color
            };

            x = leftMargin;
            foreach ((string colorName, double tint) in spotColors)
            {
                PdfColor spotColor = PdfColor.FromSeparation(colorName, tint);
                page.AddRectangle(PdfRect.FromPoints(x, y - 60, 70, 60), spotColor, PdfColor.Black);
                page.AddText(colorName, x, y - 75, "Helvetica", 9);
                page.AddText($"({tint:F1})", x + 15, y - 85, "Helvetica", 8);
                x += 90;
            }
            y -= 110;

            // === Test 3: Fill and Stroke with Separation Colors ===
            page.AddText("3. Fill and Stroke with Different Tints", leftMargin, y, "Helvetica-Bold", 12);
            y -= 30;

            // Rectangle filled with light tint, stroked with dark tint
            page.AddRectangle(PdfRect.FromPoints(leftMargin, y - 60, 100, 60),
                PdfColor.FromSeparation("PMS485", 0.3),  // Light fill
                PdfColor.FromSeparation("PMS485", 1.0),  // Dark stroke
                3);
            page.AddText("Light fill,\ndark stroke", leftMargin + 10, y - 80, "Helvetica", 9);

            // Rectangle filled with dark tint, stroked with light tint
            page.AddRectangle(PdfRect.FromPoints(leftMargin + 120, y - 60, 100, 60),
                PdfColor.FromSeparation("PMS300", 0.9),  // Dark fill
                PdfColor.FromSeparation("PMS300", 0.2),  // Light stroke
                3);
            page.AddText("Dark fill,\nlight stroke", leftMargin + 130, y - 80, "Helvetica", 9);

            y -= 90;

            // === Test 4: Separation color with opacity (should combine) ===
            page.AddText("4. Separation Color with Opacity", leftMargin, y, "Helvetica-Bold", 12);
            y -= 30;

            // Base rectangle
            page.AddRectangle(PdfRect.FromPoints(leftMargin, y - 60, 150, 60),
                PdfColor.FromSeparation("BrandOrange", 0.8),
                PdfColor.Black);

            // Overlapping rectangle with opacity
            page.AddPath()
                .Rectangle(leftMargin + 50, y - 30, 100, 40)
                .Fill(PdfColor.FromSeparation("PMS300", 0.7))
                .FillOpacity(0.5)
                .Stroke(PdfColor.Black, 1);

            page.AddText("Opacity affects\nseparation color", leftMargin + 5, y - 85, "Helvetica", 8);

            y -= 100;

            // === Test 5: Text with Separation Color ===
            page.AddText("5. Text in Separation Colors", leftMargin, y, "Helvetica-Bold", 12);
            y -= 30;

            page.AddText("PMS485", leftMargin, y)
                .Font("Helvetica-Bold", 24)
                .Color(PdfColor.FromSeparation("PMS485", 1.0));

            page.AddText("PMS300", leftMargin + 100, y)
                .Font("Helvetica-Bold", 24)
                .Color(PdfColor.FromSeparation("PMS300", 1.0));

            page.AddText("PMS375", leftMargin + 200, y)
                .Font("Helvetica-Bold", 24)
                .Color(PdfColor.FromSeparation("PMS375", 1.0));

            y -= 40;

            // Note at bottom
            page.AddText("Note: Separation colors use cs/CS and scn/SCN operators in PDF",
                leftMargin, 50, "Helvetica", 9);
            page.AddText("View in Adobe Acrobat for accurate spot color simulation",
                leftMargin, 35, "Helvetica", 9);
        });

        doc.Save(outputPath);
    }
}
