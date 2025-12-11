using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;

namespace PdfLibrary.Integration.Documents;

/// <summary>
/// Tests advanced graphics state features: CTM transformations, overprint, blend modes
/// </summary>
public class AdvancedGraphicsStateTestDocument : ITestDocument
{
    public string Name => "AdvancedGraphicsState";
    public string Description => "Tests CTM transforms, overprint modes, and blend modes";

    public void Generate(string outputPath)
    {
        PdfDocumentBuilder doc = new PdfDocumentBuilder()
            .WithMetadata(m => m.SetTitle("Advanced Graphics State Tests").SetAuthor("PdfLibrary.Integration"));

        // Page 1: CTM Transformations
        doc.AddPage(PdfPageSize.Letter, page =>
        {
            double y = 750;
            const double leftMargin = 50;

            page.AddText("CTM Transformation Tests", leftMargin, y, "Helvetica-Bold", 18);
            y -= 40;

            // === Test 1: Translation ===
            page.AddText("1. Translation (Translate)", leftMargin, y, "Helvetica-Bold", 12);
            y -= 30;

            // Original position
            page.AddRectangle(PdfRect.FromPoints(leftMargin, y - 40, 30, 30), PdfColor.LightGray, PdfColor.Black);
            page.AddText("Original", leftMargin, y - 55, "Helvetica", 8);

            // Translated
            page.AddPath()
                .Rectangle(0, 0, 30, 30)
                .Fill(PdfColor.Red)
                .Stroke(PdfColor.Black, 1)
                .Translate(leftMargin + 100, y - 40);
            page.AddText("Translate(100,0)", leftMargin + 100, y - 55, "Helvetica", 8);

            y -= 80;

            // === Test 2: Rotation ===
            page.AddText("2. Rotation (Rotate)", leftMargin, y, "Helvetica-Bold", 12);
            y -= 30;

            // 0 degrees
            page.AddPath()
                .Rectangle(-15, -15, 30, 30)
                .Fill(PdfColor.Blue)
                .Stroke(PdfColor.Black, 1)
                .Translate(leftMargin + 20, y - 25);
            page.AddText("0°", leftMargin + 5, y - 55, "Helvetica", 8);

            // 45 degrees
            page.AddPath()
                .Rectangle(-15, -15, 30, 30)
                .Fill(PdfColor.Green)
                .Stroke(PdfColor.Black, 1)
                .Translate(leftMargin + 90, y - 25)
                .Rotate(45);
            page.AddText("45°", leftMargin + 75, y - 55, "Helvetica", 8);

            // 90 degrees
            page.AddPath()
                .Rectangle(-15, -15, 30, 30)
                .Fill(PdfColor.Yellow)
                .Stroke(PdfColor.Black, 1)
                .Translate(leftMargin + 160, y - 25)
                .Rotate(90);
            page.AddText("90°", leftMargin + 145, y - 55, "Helvetica", 8);

            y -= 80;

            // === Test 3: Scale ===
            page.AddText("3. Scaling (Scale)", leftMargin, y, "Helvetica-Bold", 12);
            y -= 30;

            // Normal
            page.AddPath()
                .Rectangle(0, 0, 30, 30)
                .Fill(PdfColor.Cyan)
                .Stroke(PdfColor.Black, 1)
                .Translate(leftMargin, y - 30);
            page.AddText("1x", leftMargin + 5, y - 50, "Helvetica", 8);

            // Scaled 2x horizontal
            page.AddPath()
                .Rectangle(0, 0, 30, 30)
                .Fill(PdfColor.Magenta)
                .Stroke(PdfColor.Black, 1)
                .Translate(leftMargin + 80, y - 30)
                .Scale(2, 1);
            page.AddText("Scale(2,1)", leftMargin + 80, y - 50, "Helvetica", 8);

            // Scaled 0.5x vertical
            page.AddPath()
                .Rectangle(0, 0, 30, 30)
                .Fill(PdfColor.Yellow)
                .Stroke(PdfColor.Black, 1)
                .Translate(leftMargin + 200, y - 30)
                .Scale(1, 0.5);
            page.AddText("Scale(1,0.5)", leftMargin + 190, y - 50, "Helvetica", 8);

            y -= 80;

            // === Test 4: Combined Transforms ===
            page.AddText("4. Combined (Translate + Rotate + Scale)", leftMargin, y, "Helvetica-Bold", 12);
            y -= 30;

            page.AddPath()
                .Rectangle(-20, -20, 40, 40)
                .Fill(PdfColor.Blue)
                .FillOpacity(0.6)
                .Stroke(PdfColor.Black, 2)
                .Translate(leftMargin + 60, y - 40)
                .Rotate(30);

            page.AddPath()
                .Circle(0, 0, 25)
                .Fill(PdfColor.Red)
                .FillOpacity(0.6)
                .Translate(leftMargin + 60, y - 40);

            page.AddText("Rect rotated 30°,\ncircle unrotated", leftMargin, y - 80, "Helvetica", 8);

            y -= 100;

            // === Test 5: Path transformations ===
            page.AddText("5. Complex Paths with Transforms", leftMargin, y, "Helvetica-Bold", 12);
            y -= 30;

            // Ellipse using scaling
            page.AddPath()
                .Circle(0, 0, 20)
                .Fill(PdfColor.Green)
                .Translate(leftMargin + 30, y - 30)
                .Scale(2, 1);
            page.AddText("Ellipse via\nscaled circle", leftMargin, y - 65, "Helvetica", 7);

            // Rotated rounded rectangle
            page.AddPath()
                .RoundedRectangle(-35, -15, 70, 30, 5)
                .Fill(PdfColor.Blue)
                .Translate(leftMargin + 180, y - 30)
                .Rotate(20);
            page.AddText("Rotated\nrounded rect", leftMargin + 150, y - 65, "Helvetica", 7);
        });

        // Page 2: Overprint and Blend Modes
        doc.AddPage(PdfPageSize.Letter, page =>
        {
            double y = 750;
            const double leftMargin = 50;

            page.AddText("Overprint and Blend Mode Tests", leftMargin, y, "Helvetica-Bold", 18);
            y -= 40;

            // === Test 1: Overprint with Separation Colors ===
            page.AddText("1. Overprint with Separation Colors", leftMargin, y, "Helvetica-Bold", 12);
            y -= 25;
            page.AddText("(OPM=1, fill overprint enabled)", leftMargin, y, "Helvetica", 9);
            y -= 25;

            // Background: Orange separation color
            page.AddRectangle(PdfRect.FromPoints(leftMargin, y - 60, 200, 60),
                PdfColor.FromSeparation("Orange", 0.8), null, 0);

            // Foreground: CMYK black with overprint
            page.AddPath()
                .Rectangle(leftMargin + 50, y - 45, 100, 30)
                .Fill(PdfColor.CmykBlack)
                .WithFillOverprint(true)
                .WithOverprintMode(1);

            page.AddText("Black CMYK over\nOrange separation", leftMargin + 210, y - 40, "Helvetica", 8);

            y -= 90;

            // === Test 2: Blend Modes ===
            page.AddText("2. Blend Modes (Multiply, Screen, Overlay, Darken, Lighten)", leftMargin, y, "Helvetica-Bold", 12);
            y -= 30;

            var blendModes = new[] { "Normal", "Multiply", "Screen", "Overlay", "Darken", "Lighten" };
            double x = leftMargin;

            foreach (string blendMode in blendModes)
            {
                // Base layer (red)
                page.AddRectangle(PdfRect.FromPoints(x, y - 50, 50, 50), PdfColor.Red, null, 0);

                // Overlapping layer (blue) with blend mode
                page.AddPath()
                    .Circle(x + 35, y - 35, 20)
                    .Fill(PdfColor.Blue)
                    .FillOpacity(0.7)
                    .WithBlendMode(blendMode);

                page.AddText(blendMode, x, y - 65, "Helvetica", 7);
                x += 70;
            }

            y -= 90;

            // === Test 3: Opacity Variations ===
            page.AddText("3. Fill Opacity Variations", leftMargin, y, "Helvetica-Bold", 12);
            y -= 30;

            // Background gradient
            for (var i = 0; i < 5; i++)
            {
                page.AddRectangle(PdfRect.FromPoints(leftMargin + i * 25, y - 50, 25, 50),
                    PdfColor.FromGray(i * 0.2), null, 0);
            }

            // Overlapping shape with varying opacity
            x = leftMargin;
            for (var i = 0; i <= 4; i++)
            {
                double opacity = 0.25 * (i + 1);
                page.AddPath()
                    .Circle(x + 12, y - 25, 15)
                    .Fill(PdfColor.Green)
                    .FillOpacity(opacity);
                page.AddText($"{opacity:F2}", x, y - 60, "Helvetica", 7);
                x += 30;
            }

            y -= 90;

            // === Test 4: Stroke Overprint ===
            page.AddText("4. Stroke Overprint", leftMargin, y, "Helvetica-Bold", 12);
            y -= 30;

            // Background
            page.AddRectangle(PdfRect.FromPoints(leftMargin, y - 60, 120, 60),
                PdfColor.CmykCyan, null, 0);

            // Path with stroke overprint
            page.AddPath()
                .Circle(leftMargin + 60, y - 30, 20)
                .Fill(PdfColor.CmykYellow)
                .Stroke(PdfColor.CmykBlack, 3)
                .WithStrokeOverprint(true)
                .WithOverprintMode(1);

            page.AddText("Stroke overprint\nover cyan bg", leftMargin + 130, y - 40, "Helvetica", 8);

            y -= 90;

            // === Test 5: Combined Features ===
            page.AddText("5. Combined: Separation + Transform + Blend + Opacity", leftMargin, y, "Helvetica-Bold", 12);
            y -= 30;

            // Base layer
            page.AddRectangle(PdfRect.FromPoints(leftMargin, y - 80, 180, 80),
                PdfColor.FromSeparation("PMS485", 0.6), PdfColor.Black);

            // Rotated shape with blend mode
            page.AddPath()
                .Rectangle(-40, -30, 80, 60)
                .Fill(PdfColor.FromSeparation("PMS300", 0.8))
                .FillOpacity(0.6)
                .Translate(leftMargin + 90, y - 40)
                .Rotate(25)
                .WithBlendMode("Multiply");

            // Circle with different blend
            page.AddPath()
                .Circle(0, 0, 25)
                .Fill(PdfColor.FromSeparation("PMS375", 0.9))
                .FillOpacity(0.5)
                .Translate(leftMargin + 90, y - 40)
                .WithBlendMode("Screen");

            // Note
            page.AddText("Note: View in Adobe Acrobat for accurate rendering of advanced graphics state",
                leftMargin, 40, "Helvetica", 9);
        });

        doc.Save(outputPath);
    }
}
