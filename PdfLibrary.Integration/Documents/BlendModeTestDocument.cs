using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;

namespace PdfLibrary.Integration.Documents;

/// <summary>
/// Comprehensive test document for PDF blend modes.
/// Tests all 16 PDF 1.4+ blend modes with various configurations.
/// </summary>
public class BlendModeTestDocument : ITestDocument
{
    public string Name => "BlendModes";
    public string Description => "Tests all PDF blend modes with overlapping shapes and colors";

    public void Generate(string outputPath)
    {
        PdfDocumentBuilder doc = new PdfDocumentBuilder()
            .WithMetadata(m => m.SetTitle("Blend Mode Tests").SetAuthor("PdfLibrary.Integration"));

        // Page 1: Basic blend modes (Normal, Multiply, Screen, Overlay)
        AddBasicBlendModesPage(doc);

        // Page 2: Darken/Lighten group (Darken, Lighten, ColorDodge, ColorBurn)
        AddDarkenLightenBlendModesPage(doc);

        // Page 3: Light blend modes (HardLight, SoftLight)
        AddLightBlendModesPage(doc);

        // Page 4: Difference blend modes (Difference, Exclusion)
        AddDifferenceBlendModesPage(doc);

        // Page 5: Color blend modes (Hue, Saturation, Color, Luminosity)
        AddColorBlendModesPage(doc);

        // Page 6: Complex overlapping with multiple shapes
        AddComplexOverlapPage(doc);

        // Page 7: Blend modes with transparency
        AddTransparencyBlendModesPage(doc);

        doc.Save(outputPath);
    }

    private void AddBasicBlendModesPage(PdfDocumentBuilder doc)
    {
        doc.AddPage(PdfPageSize.Letter, page =>
        {
            double y = 750;
            const double leftMargin = 50;

            page.AddText("Blend Modes: Basic Group", leftMargin, y, "Helvetica-Bold", 20);
            y -= 40;

            page.AddText("Red rectangle (background) + Blue circle (foreground with blend mode)", leftMargin, y, "Helvetica", 10);
            y -= 50;

            var blendModes = new[] { "Normal", "Multiply", "Screen", "Overlay" };
            double x = leftMargin;

            foreach (string blendMode in blendModes)
            {
                // Red rectangle (background layer)
                page.AddRectangle(PdfRect.FromPoints(x, y - 60, 60, 60), PdfColor.Red, null);

                // Blue circle (foreground layer with blend mode)
                page.AddPath()
                    .Circle(x + 40, y - 40, 30)
                    .Fill(PdfColor.Blue)
                    .WithBlendMode(blendMode);

                // Label
                page.AddText(blendMode, x, y - 80, "Helvetica", 9);

                x += 120;
            }
        });
    }

    private void AddDarkenLightenBlendModesPage(PdfDocumentBuilder doc)
    {
        doc.AddPage(PdfPageSize.Letter, page =>
        {
            double y = 750;
            const double leftMargin = 50;

            page.AddText("Blend Modes: Darken/Lighten Group", leftMargin, y, "Helvetica-Bold", 20);
            y -= 40;

            page.AddText("Red rectangle (background) + Blue circle (foreground with blend mode)", leftMargin, y, "Helvetica", 10);
            y -= 50;

            var blendModes = new[] { "Darken", "Lighten", "ColorDodge", "ColorBurn" };
            double x = leftMargin;

            foreach (string blendMode in blendModes)
            {
                // Red rectangle (background layer)
                page.AddRectangle(PdfRect.FromPoints(x, y - 60, 60, 60), PdfColor.Red, null);

                // Blue circle (foreground layer with blend mode)
                page.AddPath()
                    .Circle(x + 40, y - 40, 30)
                    .Fill(PdfColor.Blue)
                    .WithBlendMode(blendMode);

                // Label
                page.AddText(blendMode, x - 5, y - 80, "Helvetica", 9);

                x += 120;
            }
        });
    }

    private void AddLightBlendModesPage(PdfDocumentBuilder doc)
    {
        doc.AddPage(PdfPageSize.Letter, page =>
        {
            double y = 750;
            const double leftMargin = 50;

            page.AddText("Blend Modes: Light Group", leftMargin, y, "Helvetica-Bold", 20);
            y -= 40;

            page.AddText("Red rectangle (background) + Blue circle (foreground with blend mode)", leftMargin, y, "Helvetica", 10);
            y -= 50;

            var blendModes = new[] { "HardLight", "SoftLight" };
            double x = leftMargin;

            foreach (string blendMode in blendModes)
            {
                // Red rectangle (background layer)
                page.AddRectangle(PdfRect.FromPoints(x, y - 60, 60, 60), PdfColor.Red, null);

                // Blue circle (foreground layer with blend mode)
                page.AddPath()
                    .Circle(x + 40, y - 40, 30)
                    .Fill(PdfColor.Blue)
                    .WithBlendMode(blendMode);

                // Label
                page.AddText(blendMode, x, y - 80, "Helvetica", 9);

                x += 120;
            }
        });
    }

    private void AddDifferenceBlendModesPage(PdfDocumentBuilder doc)
    {
        doc.AddPage(PdfPageSize.Letter, page =>
        {
            double y = 750;
            const double leftMargin = 50;

            page.AddText("Blend Modes: Difference Group", leftMargin, y, "Helvetica-Bold", 20);
            y -= 40;

            page.AddText("Red rectangle (background) + Blue circle (foreground with blend mode)", leftMargin, y, "Helvetica", 10);
            y -= 50;

            var blendModes = new[] { "Difference", "Exclusion" };
            double x = leftMargin;

            foreach (string blendMode in blendModes)
            {
                // Red rectangle (background layer)
                page.AddRectangle(PdfRect.FromPoints(x, y - 60, 60, 60), PdfColor.Red, null);

                // Blue circle (foreground layer with blend mode)
                page.AddPath()
                    .Circle(x + 40, y - 40, 30)
                    .Fill(PdfColor.Blue)
                    .WithBlendMode(blendMode);

                // Label
                page.AddText(blendMode, x, y - 80, "Helvetica", 9);

                x += 120;
            }
        });
    }

    private void AddColorBlendModesPage(PdfDocumentBuilder doc)
    {
        doc.AddPage(PdfPageSize.Letter, page =>
        {
            double y = 750;
            const double leftMargin = 50;

            page.AddText("Blend Modes: Color Group", leftMargin, y, "Helvetica-Bold", 20);
            y -= 40;

            page.AddText("Red rectangle (background) + Blue circle (foreground with blend mode)", leftMargin, y, "Helvetica", 10);
            y -= 50;

            var blendModes = new[] { "Hue", "Saturation", "Color", "Luminosity" };
            double x = leftMargin;

            foreach (string blendMode in blendModes)
            {
                // Red rectangle (background layer)
                page.AddRectangle(PdfRect.FromPoints(x, y - 60, 60, 60), PdfColor.Red, null);

                // Blue circle (foreground layer with blend mode)
                page.AddPath()
                    .Circle(x + 40, y - 40, 30)
                    .Fill(PdfColor.Blue)
                    .WithBlendMode(blendMode);

                // Label
                page.AddText(blendMode, x, y - 80, "Helvetica", 9);

                x += 120;
            }
        });
    }

    private void AddComplexOverlapPage(PdfDocumentBuilder doc)
    {
        doc.AddPage(PdfPageSize.Letter, page =>
        {
            double y = 750;
            const double leftMargin = 50;

            page.AddText("Blend Modes: Complex Overlapping Shapes", leftMargin, y, "Helvetica-Bold", 20);
            y -= 40;

            page.AddText("Three overlapping circles with different blend modes", leftMargin, y, "Helvetica", 10);
            y -= 50;

            var testCases = new[]
            {
                ("Multiply", "Multiply", "Multiply"),
                ("Screen", "Screen", "Screen"),
                ("Overlay", "Darken", "Lighten"),
                ("HardLight", "SoftLight", "Difference")
            };

            double x = leftMargin;
            int testNum = 1;

            foreach (var (blend1, blend2, blend3) in testCases)
            {
                double centerX = x + 40;
                double centerY = y - 40;

                // First circle (Red)
                page.AddPath()
                    .Circle(centerX, centerY, 25)
                    .Fill(PdfColor.Red)
                    .WithBlendMode(blend1);

                // Second circle (Green) - offset right
                page.AddPath()
                    .Circle(centerX + 30, centerY, 25)
                    .Fill(PdfColor.Green)
                    .WithBlendMode(blend2);

                // Third circle (Blue) - offset down
                page.AddPath()
                    .Circle(centerX + 15, centerY - 20, 25)
                    .Fill(PdfColor.Blue)
                    .WithBlendMode(blend3);

                // Labels
                page.AddText($"Test {testNum}", x, y - 80, "Helvetica-Bold", 8);
                page.AddText($"{blend1}", x, y - 90, "Helvetica", 7);
                page.AddText($"{blend2}", x, y - 98, "Helvetica", 7);
                page.AddText($"{blend3}", x, y - 106, "Helvetica", 7);

                x += 120;
                testNum++;

                if (testNum != 3) continue;
                x = leftMargin;
                y -= 150;
            }
        });
    }

    private void AddTransparencyBlendModesPage(PdfDocumentBuilder doc)
    {
        doc.AddPage(PdfPageSize.Letter, page =>
        {
            double y = 750;
            const double leftMargin = 50;

            page.AddText("Blend Modes with Transparency", leftMargin, y, "Helvetica-Bold", 20);
            y -= 40;

            page.AddText("Red rectangle (background) + Semi-transparent blue circle (50% opacity)", leftMargin, y, "Helvetica", 10);
            y -= 50;

            var blendModes = new[] { "Normal", "Multiply", "Screen", "Overlay" };
            double x = leftMargin;

            // Row 1: 50% opacity
            foreach (string blendMode in blendModes)
            {
                // Red rectangle (background layer)
                page.AddRectangle(PdfRect.FromPoints(x, y - 60, 60, 60), PdfColor.Red, null);

                // Blue circle with blend mode and 50% opacity
                page.AddPath()
                    .Circle(x + 40, y - 40, 30)
                    .Fill(PdfColor.Blue)
                    .FillOpacity(0.5)
                    .WithBlendMode(blendMode);

                // Label
                page.AddText($"{blendMode}", x, y - 80, "Helvetica", 9);
                page.AddText("50% opacity", x, y - 90, "Helvetica", 7);

                x += 120;
            }

            y -= 140;
            x = leftMargin;

            page.AddText("Red rectangle (background) + Semi-transparent blue circle (25% opacity)", leftMargin, y, "Helvetica", 10);
            y -= 30;

            // Row 2: 25% opacity
            foreach (string blendMode in blendModes)
            {
                // Red rectangle (background layer)
                page.AddRectangle(PdfRect.FromPoints(x, y - 60, 60, 60), PdfColor.Red, null);

                // Blue circle with blend mode and 25% opacity
                page.AddPath()
                    .Circle(x + 40, y - 40, 30)
                    .Fill(PdfColor.Blue)
                    .FillOpacity(0.25)
                    .WithBlendMode(blendMode);

                // Label
                page.AddText($"{blendMode}", x, y - 80, "Helvetica", 9);
                page.AddText("25% opacity", x, y - 90, "Helvetica", 7);

                x += 120;
            }
        });
    }
}
