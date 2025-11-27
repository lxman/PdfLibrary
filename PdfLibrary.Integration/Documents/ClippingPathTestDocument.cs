using System;
using PdfLibrary.Builder;

namespace PdfLibrary.Integration.Documents;

/// <summary>
/// Tests clipping paths: demonstrates AsClippingPath() method
/// Note: Current API limitation - clipping only affects content within the same path operation
/// </summary>
public class ClippingPathTestDocument : ITestDocument
{
    public string Name => "ClippingPath";
    public string Description => "Demonstrates clipping path API (AsClippingPath method)";

    public void Generate(string outputPath)
    {
        PdfDocumentBuilder doc = new PdfDocumentBuilder()
            .WithMetadata(m => m.SetTitle("Clipping Path Tests").SetAuthor("PdfLibrary.Integration"));

        doc.AddPage(PdfPageSize.Letter, page =>
        {
            double y = 750;
            const double leftMargin = 50;

            // Title
            page.AddText("Clipping Path Tests", leftMargin, y, "Helvetica-Bold", 18);
            y -= 50;

            // === Note about API limitations ===
            page.AddText("Note: Current API uses AsClippingPath() - clipping scope is per-path", leftMargin, y, "Helvetica", 10);
            y -= 30;

            // === Demonstrate basic path shapes that could be used as clips ===
            page.AddText("Path Shapes (potential clipping paths)", leftMargin, y, "Helvetica-Bold", 14);
            y -= 30;

            // Rectangular clip shape
            page.AddText("Rectangle", leftMargin + 20, y - 5, "Helvetica", 9);
            page.AddPath()
                .Rectangle(leftMargin, y - 70, 80, 60)
                .Stroke(PdfColor.Blue, 2);

            // Show what AsClippingPath does - creates the path without fill/stroke
            page.AddPath()
                .Rectangle(leftMargin + 100, y - 70, 80, 60)
                .AsClippingPath();
            page.AddText("(with AsClippingPath - invisible)", leftMargin + 100, y - 5, "Helvetica", 8);

            // Circle clip shape
            page.AddText("Circle", leftMargin + 220, y - 5, "Helvetica", 9);
            page.AddCircle(leftMargin + 260, y - 40, 30)
                .Stroke(PdfColor.Green, 2);

            // Ellipse clip shape
            page.AddText("Ellipse", leftMargin + 340, y - 5, "Helvetica", 9);
            page.AddEllipse(leftMargin + 400, y - 40, 50, 30)
                .Stroke(PdfColor.Red, 2);

            y -= 110;

            // === Complex shapes that could be clips ===
            page.AddText("Complex Shapes for Clipping", leftMargin, y, "Helvetica-Bold", 14);
            y -= 30;

            // Star shape
            DrawStar(page, leftMargin + 50, y - 50, 40, 18);
            page.AddText("Star", leftMargin + 35, y - 100, "Helvetica", 9);

            // Heart shape
            DrawHeart(page, leftMargin + 180, y - 30);
            page.AddText("Heart", leftMargin + 165, y - 100, "Helvetica", 9);

            // Rounded rectangle
            page.AddRoundedRectangle(leftMargin + 260, y - 90, 80, 60, 12)
                .Stroke(PdfColor.FromCmyk(0.5, 0, 0.5, 0), 2);
            page.AddText("Rounded Rect", leftMargin + 270, y - 100, "Helvetica", 9);

            // Polygon (hexagon)
            DrawHexagon(page, leftMargin + 420, y - 50, 35);
            page.AddText("Hexagon", leftMargin + 400, y - 100, "Helvetica", 9);

            y -= 140;

            // === Fill rules demonstration ===
            page.AddText("Fill Rules (relevant for clipping)", leftMargin, y, "Helvetica-Bold", 14);
            y -= 30;

            // Non-zero winding (default)
            page.AddText("Non-zero Winding (default)", leftMargin, y - 5, "Helvetica", 10);
            DrawConcentricSquares(page, leftMargin + 50, y - 70, false);

            // Even-odd
            page.AddText("Even-Odd Rule", leftMargin + 200, y - 5, "Helvetica", 10);
            DrawConcentricSquares(page, leftMargin + 250, y - 70, true);

            y -= 140;

            // === Explanation text ===
            page.AddText("How Clipping Works in PDF:", leftMargin, y, "Helvetica-Bold", 12);
            y -= 20;
            page.AddText("1. Define a path (rectangle, circle, or complex shape)", leftMargin + 20, y, "Helvetica", 10);
            y -= 15;
            page.AddText("2. Use W (non-zero) or W* (even-odd) operator to set as clipping path", leftMargin + 20, y, "Helvetica", 10);
            y -= 15;
            page.AddText("3. Subsequent drawing operations are clipped to this path", leftMargin + 20, y, "Helvetica", 10);
            y -= 15;
            page.AddText("4. Clipping is additive - can intersect multiple clip paths", leftMargin + 20, y, "Helvetica", 10);
            y -= 30;

            page.AddText("API Usage:", leftMargin, y, "Helvetica-Bold", 12);
            y -= 20;
            page.AddText("page.AddPath().Rectangle(...).AsClippingPath();", leftMargin + 20, y, "Courier", 9);
            y -= 15;
            page.AddText("page.AddCircle(...).AsClippingPath();", leftMargin + 20, y, "Courier", 9);
        });

        doc.Save(outputPath);
    }

    private static void DrawStar(PdfPageBuilder page, double cx, double cy, double outerR, double innerR)
    {
        PdfPathBuilder path = page.AddPath();

        for (var i = 0; i < 5; i++)
        {
            double outerAngle = (i * 72 - 90) * Math.PI / 180;
            double innerAngle = ((i * 72) + 36 - 90) * Math.PI / 180;

            double ox = cx + outerR * Math.Cos(outerAngle);
            double oy = cy + outerR * Math.Sin(outerAngle);
            double ix = cx + innerR * Math.Cos(innerAngle);
            double iy = cy + innerR * Math.Sin(innerAngle);

            if (i == 0)
                path.MoveTo(ox, oy);
            else
                path.LineTo(ox, oy);

            path.LineTo(ix, iy);
        }

        path.ClosePath()
            .Fill(PdfColor.FromRgb(255, 215, 0))
            .Stroke(PdfColor.Black, 1);
    }

    private static void DrawHeart(PdfPageBuilder page, double hx, double hy)
    {
        page.AddPath()
            .MoveTo(hx, hy - 20)
            .CurveTo(hx, hy, hx - 35, hy, hx - 35, hy - 22)
            .CurveTo(hx - 35, hy - 42, hx, hy - 58, hx, hy - 70)
            .CurveTo(hx, hy - 58, hx + 35, hy - 42, hx + 35, hy - 22)
            .CurveTo(hx + 35, hy, hx, hy, hx, hy - 20)
            .Fill(PdfColor.Red);
    }

    private static void DrawHexagon(PdfPageBuilder page, double cx, double cy, double radius)
    {
        PdfPathBuilder path = page.AddPath();

        for (var i = 0; i < 6; i++)
        {
            double angle = (i * 60 - 90) * Math.PI / 180;
            double x = cx + radius * Math.Cos(angle);
            double y = cy + radius * Math.Sin(angle);

            if (i == 0)
                path.MoveTo(x, y);
            else
                path.LineTo(x, y);
        }

        path.ClosePath()
            .Fill(PdfColor.FromGray(0.85))
            .Stroke(PdfColor.Blue, 2);
    }

    private static void DrawConcentricSquares(PdfPageBuilder page, double cx, double cy, bool useEvenOdd)
    {
        PdfPathBuilder path = page.AddPath();

        // Outer square (clockwise)
        const double size1 = 60;
        path.MoveTo(cx - size1/2, cy - size1/2)
            .LineTo(cx + size1/2, cy - size1/2)
            .LineTo(cx + size1/2, cy + size1/2)
            .LineTo(cx - size1/2, cy + size1/2)
            .ClosePath();

        // Inner square (counter-clockwise for even-odd difference)
        const double size2 = 30;
        path.MoveTo(cx - size2/2, cy - size2/2)
            .LineTo(cx - size2/2, cy + size2/2)
            .LineTo(cx + size2/2, cy + size2/2)
            .LineTo(cx + size2/2, cy - size2/2)
            .ClosePath();

        if (useEvenOdd)
            path.FillRule(PdfFillRule.EvenOdd);

        path.Fill(PdfColor.FromCmyk(0.6, 0.2, 0, 0));
    }
}
