using PdfLibrary.Builder;

namespace PdfLibrary.Integration.Documents;

/// <summary>
/// Tests path drawing: Bezier curves, arcs, circles, complex shapes
/// </summary>
public class PathDrawingTestDocument : ITestDocument
{
    public string Name => "PathDrawing";
    public string Description => "Tests Bezier curves, arcs, circles, ellipses, and complex paths";

    public void Generate(string outputPath)
    {
        var doc = new PdfDocumentBuilder()
            .WithMetadata(m => m.SetTitle("Path Drawing Tests").SetAuthor("PdfLibrary.Integration"));

        doc.AddPage(PdfPageSize.Letter, page =>
        {
            double y = 750;
            const double leftMargin = 50;

            // Title
            page.AddText("Path Drawing Tests", leftMargin, y, "Helvetica-Bold", 18);
            y -= 50;

            // === Bezier Curves Section ===
            page.AddText("Cubic Bezier Curves (c operator)", leftMargin, y, "Helvetica-Bold", 14);
            y -= 20;

            // Simple cubic Bezier
            page.AddPath()
                .MoveTo(leftMargin, y)
                .CurveTo(leftMargin + 50, y + 50, leftMargin + 100, y - 50, leftMargin + 150, y)
                .Stroke(PdfColor.Blue, 2);

            // Show control points
            page.AddText("Control points shown as dots", leftMargin + 170, y, "Helvetica", 9);

            // Draw control point markers
            page.AddCircle(leftMargin + 50, y + 50, 3).Fill(PdfColor.Red);
            page.AddCircle(leftMargin + 100, y - 50, 3).Fill(PdfColor.Red);

            y -= 80;

            // S-curve using multiple Bezier segments
            page.AddText("S-Curve (multiple Bezier segments)", leftMargin, y + 10, "Helvetica", 10);

            page.AddPath()
                .MoveTo(leftMargin, y - 30)
                .CurveTo(leftMargin + 40, y + 20, leftMargin + 60, y + 20, leftMargin + 100, y - 30)
                .CurveTo(leftMargin + 140, y - 80, leftMargin + 160, y - 80, leftMargin + 200, y - 30)
                .Stroke(PdfColor.FromCmyk(0, 0.8, 0, 0), 2.5);

            y -= 100;

            // === Circles and Ellipses Section ===
            page.AddText("Circles and Ellipses (Bezier approximation)", leftMargin, y, "Helvetica-Bold", 14);
            y -= 30;

            // Circle - filled
            page.AddCircle(leftMargin + 40, y - 40, 35)
                .Fill(PdfColor.FromRgb(100, 149, 237)); // Cornflower blue

            page.AddText("Filled Circle", leftMargin + 10, y - 90, "Helvetica", 9);

            // Circle - stroked
            page.AddCircle(leftMargin + 140, y - 40, 35)
                .Stroke(PdfColor.Green, 3);

            page.AddText("Stroked Circle", leftMargin + 110, y - 90, "Helvetica", 9);

            // Circle - filled and stroked
            page.AddCircle(leftMargin + 240, y - 40, 35)
                .Fill(PdfColor.Yellow)
                .Stroke(PdfColor.Red, 2);

            page.AddText("Fill + Stroke", leftMargin + 215, y - 90, "Helvetica", 9);

            // Ellipse
            page.AddEllipse(leftMargin + 360, y - 40, 50, 30)
                .Fill(PdfColor.FromGray(0.8))
                .Stroke(PdfColor.Black, 1.5);

            page.AddText("Ellipse", leftMargin + 345, y - 90, "Helvetica", 9);

            y -= 120;

            // === Arcs Section ===
            page.AddText("Arcs (partial circles)", leftMargin, y, "Helvetica-Bold", 14);
            y -= 30;

            // Quarter arc (90 degrees)
            page.AddPath()
                .Arc(leftMargin + 50, y - 50, 40, 0, 90)
                .Stroke(PdfColor.Blue, 3);
            page.AddText("90 deg", leftMargin + 35, y - 100, "Helvetica", 9);

            // Half arc (180 degrees)
            page.AddPath()
                .Arc(leftMargin + 150, y - 50, 40, 0, 180)
                .Stroke(PdfColor.Green, 3);
            page.AddText("180 deg", leftMargin + 130, y - 100, "Helvetica", 9);

            // Three-quarter arc (270 degrees)
            page.AddPath()
                .Arc(leftMargin + 260, y - 50, 40, 45, 315)
                .Stroke(PdfColor.Red, 3);
            page.AddText("270 deg (45-315)", leftMargin + 220, y - 100, "Helvetica", 9);

            y -= 130;

            // === Complex Shapes Section ===
            page.AddText("Complex Shapes", leftMargin, y, "Helvetica-Bold", 14);
            y -= 30;

            // Star shape
            double cx = leftMargin + 50;
            double cy = y - 50;
            double outerR = 40;
            double innerR = 18;
            var starPath = page.AddPath();

            for (int i = 0; i < 5; i++)
            {
                double outerAngle = (i * 72 - 90) * Math.PI / 180;
                double innerAngle = ((i * 72) + 36 - 90) * Math.PI / 180;

                double ox = cx + outerR * Math.Cos(outerAngle);
                double oy = cy + outerR * Math.Sin(outerAngle);
                double ix = cx + innerR * Math.Cos(innerAngle);
                double iy = cy + innerR * Math.Sin(innerAngle);

                if (i == 0)
                    starPath.MoveTo(ox, oy);
                else
                    starPath.LineTo(ox, oy);

                starPath.LineTo(ix, iy);
            }

            starPath.ClosePath()
                .Fill(PdfColor.FromRgb(255, 215, 0))
                .Stroke(PdfColor.Black, 1);

            page.AddText("Star", leftMargin + 35, y - 100, "Helvetica", 9);

            // Rounded rectangle
            double rx = leftMargin + 130;
            double ry = y - 80;
            double rw = 80;
            double rh = 50;
            double radius = 10;

            page.AddRoundedRectangle(rx, ry, rw, rh, radius)
                .Fill(PdfColor.FromGray(0.9))
                .Stroke(PdfColor.Blue, 2);
            page.AddText("Rounded Rect", leftMargin + 140, y - 100, "Helvetica", 9);

            // Heart shape using Bezier curves
            double hx = leftMargin + 300;
            double hy = y - 45;

            page.AddPath()
                .MoveTo(hx, hy - 20)
                .CurveTo(hx, hy, hx - 30, hy, hx - 30, hy - 20)
                .CurveTo(hx - 30, hy - 35, hx, hy - 50, hx, hy - 60)
                .CurveTo(hx, hy - 50, hx + 30, hy - 35, hx + 30, hy - 20)
                .CurveTo(hx + 30, hy, hx, hy, hx, hy - 20)
                .Fill(PdfColor.Red);
            page.AddText("Heart", leftMargin + 290, y - 100, "Helvetica", 9);

            // Spiral using connected semi-circle Bezier approximations
            double spiralX = leftMargin + 420;
            double spiralY = y - 50;
            var spiralPath = page.AddPath().MoveTo(spiralX, spiralY);

            // Draw spiral as connected semi-circles with increasing radii
            // Each semi-circle is approximated with two quarter-arc Bezier curves
            double[] radii = [8, 13, 18, 23, 28, 33];
            const double kappa = 0.5522847498; // Magic number for circle approximation

            double currentX = spiralX;
            double currentY = spiralY;

            for (int i = 0; i < radii.Length; i++)
            {
                double r = radii[i];
                bool goingRight = (i % 2 == 0);

                // Center is offset from current position by the radius
                double arcCx = currentX + (goingRight ? r : -r);
                double arcCy = currentY;

                // Draw semi-circle as two quarter arcs
                // First quarter (90 degrees)
                double k1 = r * kappa;
                if (goingRight)
                {
                    // Top half: go up and right
                    spiralPath.CurveTo(currentX, currentY + k1, arcCx - k1, arcCy + r, arcCx, arcCy + r);
                    spiralPath.CurveTo(arcCx + k1, arcCy + r, arcCx + r, arcCy + k1, arcCx + r, arcCy);
                }
                else
                {
                    // Bottom half: go down and left
                    spiralPath.CurveTo(currentX, currentY - k1, arcCx + k1, arcCy - r, arcCx, arcCy - r);
                    spiralPath.CurveTo(arcCx - k1, arcCy - r, arcCx - r, arcCy - k1, arcCx - r, arcCy);
                }

                // Update current position to the end of this semi-circle
                currentX = arcCx + (goingRight ? r : -r);
                currentY = arcCy;
            }

            spiralPath.Stroke(PdfColor.FromCmyk(0.7, 0, 0.7, 0), 2);
            page.AddText("Spiral", leftMargin + 400, y - 100, "Helvetica", 9);
        });

        doc.Save(outputPath);
    }
}
