using PdfLibrary.Builder;

namespace PdfLibrary.Integration.Documents;

/// <summary>
/// Tests line styles: width, cap, join, dash patterns, miter limit
/// </summary>
public class LineStyleTestDocument : ITestDocument
{
    public string Name => "LineStyles";
    public string Description => "Tests line width, cap styles, join styles, dash patterns, and miter limits";

    public void Generate(string outputPath)
    {
        var doc = new PdfDocumentBuilder()
            .WithMetadata(m => m.SetTitle("Line Style Tests").SetAuthor("PdfLibrary.Integration"));

        doc.AddPage(PdfPageSize.Letter, page =>
        {
            double y = 760;
            const double leftMargin = 50;

            // Title
            page.AddText("Line Style Tests", leftMargin, y, "Helvetica-Bold", 18);
            y -= 40;

            // === Line Width Section ===
            page.AddText("Line Width (w operator)", leftMargin, y, "Helvetica-Bold", 14);
            y -= 25;

            double[] widths = [0.5, 1, 2, 4, 8, 12];
            double x = leftMargin;

            foreach (double width in widths)
            {
                page.AddPath()
                    .MoveTo(x, y)
                    .LineTo(x + 80, y)
                    .Stroke(PdfColor.Black, width);
                page.AddText($"{width}pt", x + 30, y - 15, "Helvetica", 9);
                x += 90;
            }

            y -= 40;

            // === Line Cap Styles Section ===
            page.AddText("Line Cap Styles (J operator)", leftMargin, y, "Helvetica-Bold", 14);
            y -= 25;

            var capStyles = new (PdfLineCap cap, string name)[]
            {
                (PdfLineCap.Butt, "Butt (0)"),
                (PdfLineCap.Round, "Round (1)"),
                (PdfLineCap.Square, "Square (2)"),
            };

            x = leftMargin;
            foreach ((PdfLineCap cap, string name) in capStyles)
            {
                // Draw thick line to show cap style
                page.AddPath()
                    .MoveTo(x + 20, y - 20)
                    .LineTo(x + 100, y - 20)
                    .LineCap(cap)
                    .Stroke(PdfColor.Blue, 15);

                // Draw thin reference line on top
                page.AddPath()
                    .MoveTo(x + 20, y - 20)
                    .LineTo(x + 100, y - 20)
                    .Stroke(PdfColor.Red, 0.5);

                // Draw end markers
                page.AddPath()
                    .MoveTo(x + 20, y - 5)
                    .LineTo(x + 20, y - 35)
                    .Stroke(PdfColor.FromGray(0.5), 0.5);
                page.AddPath()
                    .MoveTo(x + 100, y - 5)
                    .LineTo(x + 100, y - 35)
                    .Stroke(PdfColor.FromGray(0.5), 0.5);

                page.AddText(name, x + 35, y - 45, "Helvetica", 9);
                x += 150;
            }

            y -= 70;

            // === Line Join Styles Section ===
            page.AddText("Line Join Styles (j operator)", leftMargin, y, "Helvetica-Bold", 14);
            y -= 25;

            var joinStyles = new (PdfLineJoin join, string name)[]
            {
                (PdfLineJoin.Miter, "Miter (0)"),
                (PdfLineJoin.Round, "Round (1)"),
                (PdfLineJoin.Bevel, "Bevel (2)"),
            };

            x = leftMargin;
            foreach ((PdfLineJoin join, string name) in joinStyles)
            {
                // Draw angle to show join style
                page.AddPath()
                    .MoveTo(x, y - 50)
                    .LineTo(x + 40, y)
                    .LineTo(x + 80, y - 50)
                    .LineJoin(join)
                    .Stroke(PdfColor.Green, 12);

                page.AddText(name, x + 20, y - 60, "Helvetica", 9);
                x += 150;
            }

            y -= 85;

            // === Dash Patterns Section ===
            page.AddText("Dash Patterns (d operator)", leftMargin, y, "Helvetica-Bold", 14);
            y -= 20;

            var dashPatterns = new (double[] pattern, string name)[]
            {
                ([4, 4], "[4 4] - Basic"),
                ([8, 4], "[8 4] - Long-Short"),
                ([2, 2], "[2 2] - Dotted"),
                ([12, 4, 4, 4], "[12 4 4 4] - Dash-Dot"),
                ([], "[] - Solid"),
            };

            foreach ((double[] pattern, string name) in dashPatterns)
            {
                page.AddPath()
                    .MoveTo(leftMargin, y)
                    .LineTo(leftMargin + 200, y)
                    .DashPattern(pattern)
                    .Stroke(PdfColor.Black, 2);
                page.AddText(name, leftMargin + 220, y - 3, "Helvetica", 9);
                y -= 20;
            }

            y -= 10;

            // === Miter Limit Section ===
            page.AddText("Miter Limit (M operator)", leftMargin, y, "Helvetica-Bold", 14);
            y -= 15;
            page.AddText("Sharp angles with different miter limits (default=10)", leftMargin, y, "Helvetica", 10);
            y -= 25;

            var miterLimits = new double[] { 1, 2, 4, 10 };
            x = leftMargin;

            foreach (double miter in miterLimits)
            {
                // Draw very sharp angle
                page.AddPath()
                    .MoveTo(x, y - 40)
                    .LineTo(x + 30, y)
                    .LineTo(x + 60, y - 40)
                    .MiterLimit(miter)
                    .LineJoin(PdfLineJoin.Miter)
                    .Stroke(PdfColor.FromCmyk(0, 0.7, 0.7, 0), 8);

                page.AddText($"M={miter}", x + 15, y - 50, "Helvetica", 9);
                x += 100;
            }

            y -= 75;

            // === Combined Styles Section ===
            page.AddText("Combined Line Styles", leftMargin, y, "Helvetica-Bold", 14);
            y -= 25;

            // Dashed with round caps
            page.AddPath()
                .MoveTo(leftMargin, y)
                .LineTo(leftMargin + 150, y)
                .LineCap(PdfLineCap.Round)
                .DashPattern([20, 10])
                .Stroke(PdfColor.Blue, 6);
            page.AddText("Round cap + dash", leftMargin + 170, y - 3, "Helvetica", 9);
            y -= 25;

            // Dashed path with round joins
            page.AddPath()
                .MoveTo(leftMargin, y - 15)
                .LineTo(leftMargin + 50, y + 10)
                .LineTo(leftMargin + 100, y - 15)
                .LineTo(leftMargin + 150, y + 10)
                .LineJoin(PdfLineJoin.Round)
                .DashPattern([8, 4])
                .Stroke(PdfColor.Red, 4);
            page.AddText("Round join + dash zigzag", leftMargin + 170, y - 3, "Helvetica", 9);
            y -= 40;

            // Complex dashed rectangle
            page.AddPath()
                .Rectangle(leftMargin, y - 40, 100, 35)
                .LineCap(PdfLineCap.Round)
                .LineJoin(PdfLineJoin.Round)
                .DashPattern([10, 5, 2, 5])
                .Stroke(PdfColor.FromCmyk(0.5, 0, 0.5, 0), 3);
            page.AddText("Complex dash pattern on rect", leftMargin + 120, y - 22, "Helvetica", 9);

            y -= 60;

            // === Dotted line convenience method ===
            page.AddText("Dotted Line (convenience method)", leftMargin, y, "Helvetica-Bold", 14);
            y -= 20;

            page.AddPath()
                .MoveTo(leftMargin, y)
                .LineTo(leftMargin + 200, y)
                .Dotted(3, 2)
                .Stroke(PdfColor.Black, 3);
            page.AddText("Dotted(3, 2)", leftMargin + 220, y - 3, "Helvetica", 9);
            y -= 20;

            page.AddPath()
                .MoveTo(leftMargin, y)
                .LineTo(leftMargin + 200, y)
                .Dotted(6, 4)
                .Stroke(PdfColor.Blue, 6);
            page.AddText("Dotted(6, 4)", leftMargin + 220, y - 3, "Helvetica", 9);
        });

        doc.Save(outputPath);
    }
}
