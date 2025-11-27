using PdfLibrary.Builder;

namespace PdfLibrary.Integration.Documents;

/// <summary>
/// Tests transparency/opacity: fill alpha, stroke alpha, overlapping transparent shapes
/// </summary>
public class TransparencyTestDocument : ITestDocument
{
    public string Name => "Transparency";
    public string Description => "Tests fill and stroke opacity using ExtGState (ca/CA operators)";

    public void Generate(string outputPath)
    {
        PdfDocumentBuilder doc = new PdfDocumentBuilder()
            .WithMetadata(m => m.SetTitle("Transparency Tests").SetAuthor("PdfLibrary.Integration"));

        doc.AddPage(PdfPageSize.Letter, page =>
        {
            double y = 760;
            const double leftMargin = 50;

            // Title
            page.AddText("Transparency Tests", leftMargin, y, "Helvetica-Bold", 18);
            y -= 40;

            // === Fill Opacity Section ===
            page.AddText("Fill Opacity (ca operator)", leftMargin, y, "Helvetica-Bold", 14);
            y -= 25;

            // Draw a background pattern to show transparency
            DrawCheckerboard(page, leftMargin, y - 60, 400, 55, 10);

            // Overlapping rectangles with decreasing opacity
            double[] opacities = [1.0, 0.75, 0.5, 0.25, 0.1];
            double x = leftMargin + 10;

            foreach (double opacity in opacities)
            {
                page.AddPath()
                    .Rectangle(x, y - 55, 50, 50)
                    .FillOpacity(opacity)
                    .Fill(PdfColor.Red);

                page.AddText($"{opacity:P0}", x + 12, y - 70, "Helvetica", 8);
                x += 60;
            }

            y -= 95;

            // === Stroke Opacity Section ===
            page.AddText("Stroke Opacity (CA operator)", leftMargin, y, "Helvetica-Bold", 14);
            y -= 25;

            // Background
            DrawCheckerboard(page, leftMargin, y - 60, 400, 55, 10);

            x = leftMargin + 10;
            foreach (double opacity in opacities)
            {
                page.AddPath()
                    .Rectangle(x, y - 55, 50, 50)
                    .StrokeOpacity(opacity)
                    .Stroke(PdfColor.Blue, 4);

                page.AddText($"{opacity:P0}", x + 12, y - 70, "Helvetica", 8);
                x += 60;
            }

            y -= 95;

            // === Combined Fill and Stroke Opacity ===
            page.AddText("Combined Fill and Stroke Opacity", leftMargin, y, "Helvetica-Bold", 14);
            y -= 25;

            DrawCheckerboard(page, leftMargin, y - 60, 400, 55, 10);

            x = leftMargin + 10;
            var combinations = new (double fill, double stroke)[]
            {
                (1.0, 1.0),
                (0.5, 1.0),
                (1.0, 0.5),
                (0.5, 0.5),
                (0.25, 0.75),
            };

            foreach ((double fillOp, double strokeOp) in combinations)
            {
                page.AddPath()
                    .Rectangle(x, y - 55, 50, 50)
                    .FillOpacity(fillOp)
                    .StrokeOpacity(strokeOp)
                    .Fill(PdfColor.Green)
                    .Stroke(PdfColor.Red, 3);

                page.AddText($"F:{fillOp:F2}", x + 8, y - 68, "Helvetica", 7);
                page.AddText($"S:{strokeOp:F2}", x + 8, y - 78, "Helvetica", 7);
                x += 60;
            }

            y -= 105;

            // === Overlapping Transparent Shapes ===
            page.AddText("Overlapping Transparent Shapes", leftMargin, y, "Helvetica-Bold", 14);
            y -= 15;

            // Three overlapping circles (RGB) with transparency
            double circleRadius = 40;
            double cx = leftMargin + 80;
            double cy = y - 55;

            // Red circle (top)
            page.AddCircle(cx, cy - 15, circleRadius)
                .FillOpacity(0.5)
                .Fill(PdfColor.Red);

            // Green circle (bottom-left)
            page.AddCircle(cx - 28, cy + 25, circleRadius)
                .FillOpacity(0.5)
                .Fill(PdfColor.Green);

            // Blue circle (bottom-right)
            page.AddCircle(cx + 28, cy + 25, circleRadius)
                .FillOpacity(0.5)
                .Fill(PdfColor.Blue);

            page.AddText("RGB Overlap (50% opacity each)", leftMargin + 35, y - 110, "Helvetica", 8);

            // CMYK overlapping squares
            double sqX = leftMargin + 240;
            double sqY = y - 25;
            double sqSize = 50;

            page.AddPath()
                .Rectangle(sqX, sqY - sqSize, sqSize, sqSize)
                .FillOpacity(0.6)
                .Fill(PdfColor.CmykCyan);

            page.AddPath()
                .Rectangle(sqX + 25, sqY - sqSize - 15, sqSize, sqSize)
                .FillOpacity(0.6)
                .Fill(PdfColor.CmykMagenta);

            page.AddPath()
                .Rectangle(sqX + 50, sqY - sqSize, sqSize, sqSize)
                .FillOpacity(0.6)
                .Fill(PdfColor.CmykYellow);

            page.AddText("CMYK Overlap (60% opacity)", leftMargin + 250, y - 110, "Helvetica", 8);

            y -= 130;

            // === Gradient-like Effect with Opacity ===
            page.AddText("Gradient Effect Using Opacity Steps", leftMargin, y, "Helvetica-Bold", 14);
            y -= 15;

            // Horizontal gradient effect
            double gradX = leftMargin;
            double gradWidth = 350;
            var steps = 20;
            double stepWidth = gradWidth / steps;

            for (var i = 0; i < steps; i++)
            {
                double opacity = (double)(steps - i) / steps;
                page.AddPath()
                    .Rectangle(gradX + (i * stepWidth), y - 30, stepWidth + 1, 25)
                    .FillOpacity(opacity)
                    .Fill(PdfColor.FromCmyk(0.8, 0, 0.8, 0)); // Purple in CMYK
            }

            page.AddText("1.0", leftMargin, y - 45, "Helvetica", 8);
            page.AddText("0.05", leftMargin + gradWidth - 20, y - 45, "Helvetica", 8);
        });

        doc.Save(outputPath);
    }

    private static void DrawCheckerboard(PdfPageBuilder page, double x, double y, double width, double height, double cellSize)
    {
        var cols = (int)(width / cellSize);
        var rows = (int)(height / cellSize);

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                bool isDark = (row + col) % 2 == 0;
                PdfColor color = isDark ? PdfColor.FromGray(0.85) : PdfColor.White;

                page.AddRectangle(
                    PdfRect.FromPoints(x + (col * cellSize), y + (row * cellSize), cellSize, cellSize),
                    color,
                    null,
                    0);
            }
        }
    }
}
