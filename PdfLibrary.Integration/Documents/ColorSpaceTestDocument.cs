using PdfLibrary.Builder;

namespace PdfLibrary.Integration.Documents;

/// <summary>
/// Tests different color spaces: DeviceGray, DeviceRGB, DeviceCMYK
/// </summary>
public class ColorSpaceTestDocument : ITestDocument
{
    public string Name => "ColorSpaces";
    public string Description => "Tests DeviceGray, DeviceRGB, and DeviceCMYK color spaces";

    public void Generate(string outputPath)
    {
        PdfDocumentBuilder doc = new PdfDocumentBuilder()
            .WithMetadata(m => m.SetTitle("Color Space Tests").SetAuthor("PdfLibrary.Integration"));

        doc.AddPage(PdfPageSize.Letter, page =>
        {
            double y = 750;
            const double leftMargin = 50;
            const double swatchSize = 40;
            const double spacing = 60;

            // Title
            page.AddText("Color Space Tests", leftMargin, y, "Helvetica-Bold", 18);
            y -= 40;

            // === DeviceRGB Section ===
            page.AddText("DeviceRGB Color Space (rg/RG operators)", leftMargin, y, "Helvetica-Bold", 14);
            y -= 30;

            // RGB color swatches
            var rgbColors = new (PdfColor color, string name)[]
            {
                (PdfColor.Red, "Red (1,0,0)"),
                (PdfColor.Green, "Green (0,1,0)"),
                (PdfColor.Blue, "Blue (0,0,1)"),
                (PdfColor.Yellow, "Yellow (1,1,0)"),
                (PdfColor.Cyan, "Cyan (0,1,1)"),
                (PdfColor.Magenta, "Magenta (1,0,1)"),
                (new PdfColor(1.0, 0.5, 0.0), "Orange (1,0.5,0)"),
                (new PdfColor(0.5, 0.0, 0.5), "Purple (0.5,0,0.5)"),
            };

            double x = leftMargin;
            foreach ((PdfColor color, string name) in rgbColors)
            {
                page.AddRectangle(PdfRect.FromPoints(x, y - swatchSize, swatchSize, swatchSize), color, PdfColor.Black);
                page.AddText(name, x, y - swatchSize - 15, "Helvetica", 8);
                x += spacing + 40;
                if (x > 500)
                {
                    x = leftMargin;
                    y -= 70;
                }
            }
            y -= 80;

            // === DeviceGray Section ===
            page.AddText("DeviceGray Color Space (g/G operators)", leftMargin, y, "Helvetica-Bold", 14);
            y -= 30;

            // Gray gradient swatches
            x = leftMargin;
            for (var i = 0; i <= 10; i++)
            {
                double grayValue = i / 10.0;
                PdfColor gray = PdfColor.FromGray(grayValue);
                page.AddRectangle(PdfRect.FromPoints(x, y - swatchSize, swatchSize, swatchSize), gray, PdfColor.Black, 0.5);
                page.AddText($"{grayValue:F1}", x + 10, y - swatchSize - 12, "Helvetica", 8);
                x += swatchSize + 10;
            }
            y -= 80;

            // Gray labels
            page.AddText("0.0 = Black, 1.0 = White", leftMargin, y, "Helvetica", 10);
            y -= 40;

            // === DeviceCMYK Section ===
            page.AddText("DeviceCMYK Color Space (k/K operators)", leftMargin, y, "Helvetica-Bold", 14);
            y -= 30;

            // CMYK color swatches
            var cmykColors = new (PdfColor color, string name)[]
            {
                (PdfColor.CmykCyan, "Cyan (1,0,0,0)"),
                (PdfColor.CmykMagenta, "Magenta (0,1,0,0)"),
                (PdfColor.CmykYellow, "Yellow (0,0,1,0)"),
                (PdfColor.CmykBlack, "Black (0,0,0,1)"),
                (PdfColor.FromCmyk(1, 1, 0, 0), "Blue (1,1,0,0)"),
                (PdfColor.FromCmyk(0, 1, 1, 0), "Red (0,1,1,0)"),
                (PdfColor.FromCmyk(1, 0, 1, 0), "Green (1,0,1,0)"),
                (PdfColor.FromCmyk(0.5, 0.5, 0, 0), "Purple (0.5,0.5,0,0)"),
            };

            x = leftMargin;
            foreach ((PdfColor color, string name) in cmykColors)
            {
                page.AddRectangle(PdfRect.FromPoints(x, y - swatchSize, swatchSize, swatchSize), color, PdfColor.CmykBlack);
                page.AddText(name, x, y - swatchSize - 15, "Helvetica", 7);
                x += spacing + 50;
                if (!(x > 500)) continue;
                x = leftMargin;
                y -= 70;
            }
            y -= 80;

            // === Mixed Color Spaces Section ===
            page.AddText("Mixed Color Spaces (same page)", leftMargin, y, "Helvetica-Bold", 14);
            y -= 30;

            // Show all three color spaces side by side
            x = leftMargin;

            // RGB Red
            page.AddRectangle(PdfRect.FromPoints(x, y - 60, 60, 60), PdfColor.Red, null, 0);
            page.AddText("RGB Red", x, y - 75, "Helvetica", 9);
            x += 80;

            // Gray 50%
            page.AddRectangle(PdfRect.FromPoints(x, y - 60, 60, 60), PdfColor.FromGray(0.5), null, 0);
            page.AddText("Gray 0.5", x, y - 75, "Helvetica", 9);
            x += 80;

            // CMYK Cyan
            page.AddRectangle(PdfRect.FromPoints(x, y - 60, 60, 60), PdfColor.CmykCyan, null, 0);
            page.AddText("CMYK Cyan", x, y - 75, "Helvetica", 9);
            x += 80;

            // RGB Blue
            page.AddRectangle(PdfRect.FromPoints(x, y - 60, 60, 60), PdfColor.Blue, null, 0);
            page.AddText("RGB Blue", x, y - 75, "Helvetica", 9);
            x += 80;

            // CMYK Yellow
            page.AddRectangle(PdfRect.FromPoints(x, y - 60, 60, 60), PdfColor.CmykYellow, null, 0);
            page.AddText("CMYK Yellow", x, y - 75, "Helvetica", 9);
        });

        doc.Save(outputPath);
    }
}
