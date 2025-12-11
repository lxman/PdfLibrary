using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;

namespace PdfTool;

/// <summary>
/// Test harness for advanced PDF features: Separation colors, CTM transforms, overprint, blend modes
/// </summary>
public static class TestAdvancedFeatures
{
    public static void CreateTestDocument(string outputPath)
    {
        var doc = new PdfDocumentBuilder("Test Advanced PDF Features");

        // Page 1: Separation Colors
        CreateSeparationColorTestPage(doc);

        // Page 2: CTM Transformations
        CreateTransformTestPage(doc);

        // Page 3: Overprint and Blend Modes
        CreateOverprintAndBlendTestPage(doc);

        // Page 4: Combined Features
        CreateCombinedFeaturesTestPage(doc);

        doc.SaveToFile(outputPath);
        Console.WriteLine($"Test document created: {outputPath}");
    }

    private static void CreateSeparationColorTestPage(PdfDocumentBuilder doc)
    {
        var page = doc.AddPage(PdfPageSize.Letter);

        // Title
        page.AddText("Separation Color Space Test", 50, 750, "Helvetica-Bold", 18)
            .Fill(PdfColor.Black);

        // Test 1: Simple separation color rectangle
        page.AddText("1. Simple Separation Color (Orange tint=0.5)", 50, 700, "Helvetica", 12)
            .Fill(PdfColor.Black);

        var orangeColor = PdfColor.FromSeparation("Orange", 0.5);
        page.AddRectangle(50, 650, 200, 40)
            .Fill(orangeColor);

        // Test 2: Varying tint values
        page.AddText("2. Tint Variations (0.2, 0.5, 0.8)", 50, 600, "Helvetica", 12)
            .Fill(PdfColor.Black);

        page.AddRectangle(50, 550, 60, 40).Fill(PdfColor.FromSeparation("Orange", 0.2));
        page.AddRectangle(120, 550, 60, 40).Fill(PdfColor.FromSeparation("Orange", 0.5));
        page.AddRectangle(190, 550, 60, 40).Fill(PdfColor.FromSeparation("Orange", 0.8));

        // Test 3: Multiple colorants
        page.AddText("3. Multiple Spot Colors", 50, 500, "Helvetica", 12)
            .Fill(PdfColor.Black);

        page.AddRectangle(50, 450, 60, 40).Fill(PdfColor.FromSeparation("PMS485", 1.0));
        page.AddRectangle(120, 450, 60, 40).Fill(PdfColor.FromSeparation("PMS300", 1.0));
        page.AddRectangle(190, 450, 60, 40).Fill(PdfColor.FromSeparation("PMS375", 1.0));

        // Test 4: Separation color with stroke
        page.AddText("4. Fill and Stroke with Separation", 50, 400, "Helvetica", 12)
            .Fill(PdfColor.Black);

        page.AddRectangle(50, 350, 100, 40)
            .Fill(PdfColor.FromSeparation("Orange", 0.3))
            .Stroke(PdfColor.FromSeparation("Orange", 1.0), 2);
    }

    private static void CreateTransformTestPage(PdfDocumentBuilder doc)
    {
        var page = doc.AddPage(PdfPageSize.Letter);

        // Title
        page.AddText("CTM Transformation Test", 50, 750, "Helvetica-Bold", 18)
            .Fill(PdfColor.Black);

        // Test 1: Translation
        page.AddText("1. Translation", 50, 700, "Helvetica", 12)
            .Fill(PdfColor.Black);

        page.BeginPath()
            .Rectangle(0, 0, 50, 50)
            .Fill(PdfColor.Red)
            .Translate(100, 630);

        // Test 2: Rotation
        page.AddText("2. Rotation (45 degrees)", 200, 700, "Helvetica", 12)
            .Fill(PdfColor.Black);

        page.BeginPath()
            .Rectangle(-25, -25, 50, 50)
            .Fill(PdfColor.Blue)
            .Translate(275, 655)
            .Rotate(45);

        // Test 3: Scale
        page.AddText("3. Scale (2x horizontal, 0.5x vertical)", 50, 580, "Helvetica", 12)
            .Fill(PdfColor.Black);

        page.BeginPath()
            .Rectangle(0, 0, 50, 50)
            .Fill(PdfColor.Green)
            .Translate(100, 510)
            .Scale(2, 0.5);

        // Test 4: Combined transforms
        page.AddText("4. Combined (translate + rotate + scale)", 50, 460, "Helvetica", 12)
            .Fill(PdfColor.Black);

        page.BeginPath()
            .Rectangle(-25, -25, 50, 50)
            .Fill(PdfColor.Magenta)
            .Translate(150, 415)
            .Rotate(30);
    }

    private static void CreateOverprintAndBlendTestPage(PdfDocumentBuilder doc)
    {
        var page = doc.AddPage(PdfPageSize.Letter);

        // Title
        page.AddText("Overprint and Blend Mode Test", 50, 750, "Helvetica-Bold", 18)
            .Fill(PdfColor.Black);

        // Test 1: Overprint (will overlap with separation color)
        page.AddText("1. Overprint Test (orange background, CMYK black with overprint)", 50, 700, "Helvetica", 12)
            .Fill(PdfColor.Black);

        // Background: Separation color
        page.AddRectangle(50, 640, 150, 50)
            .Fill(PdfColor.FromSeparation("Orange", 0.8));

        // Foreground: CMYK black with overprint enabled
        page.BeginPath()
            .Rectangle(100, 650, 100, 30)
            .Fill(PdfColor.CmykBlack)
            .WithFillOverprint(true)
            .WithOverprintMode(1);

        // Test 2: Blend modes
        page.AddText("2. Blend Modes (Multiply, Screen, Overlay)", 50, 600, "Helvetica", 12)
            .Fill(PdfColor.Black);

        // Base layer
        page.AddRectangle(50, 540, 60, 50).Fill(PdfColor.Red);
        page.AddRectangle(120, 540, 60, 50).Fill(PdfColor.Red);
        page.AddRectangle(190, 540, 60, 50).Fill(PdfColor.Red);

        // Overlapping with different blend modes
        page.BeginPath()
            .Circle(80, 565, 25)
            .Fill(PdfColor.Blue)
            .FillOpacity(0.7)
            .WithBlendMode("Multiply");

        page.BeginPath()
            .Circle(150, 565, 25)
            .Fill(PdfColor.Blue)
            .FillOpacity(0.7)
            .WithBlendMode("Screen");

        page.BeginPath()
            .Circle(220, 565, 25)
            .Fill(PdfColor.Blue)
            .FillOpacity(0.7)
            .WithBlendMode("Overlay");

        // Test 3: Opacity variations
        page.AddText("3. Opacity (0.25, 0.5, 0.75, 1.0)", 50, 490, "Helvetica", 12)
            .Fill(PdfColor.Black);

        page.BeginPath().Rectangle(50, 430, 40, 50).Fill(PdfColor.Green).FillOpacity(0.25);
        page.BeginPath().Rectangle(100, 430, 40, 50).Fill(PdfColor.Green).FillOpacity(0.5);
        page.BeginPath().Rectangle(150, 430, 40, 50).Fill(PdfColor.Green).FillOpacity(0.75);
        page.BeginPath().Rectangle(200, 430, 40, 50).Fill(PdfColor.Green).FillOpacity(1.0);
    }

    private static void CreateCombinedFeaturesTestPage(PdfDocumentBuilder doc)
    {
        var page = doc.AddPage(PdfPageSize.Letter);

        // Title
        page.AddText("Combined Features Test", 50, 750, "Helvetica-Bold", 18)
            .Fill(PdfColor.Black);

        // Test: Separation color + transform + blend mode + opacity
        page.AddText("Separation Color + Rotation + Opacity + Blend Mode", 50, 700, "Helvetica", 12)
            .Fill(PdfColor.Black);

        // Base layer
        page.AddRectangle(100, 550, 200, 100)
            .Fill(PdfColor.FromSeparation("PMS485", 0.6));

        // Overlapping rotated shape with blend mode
        page.BeginPath()
            .Rectangle(-50, -50, 100, 100)
            .Fill(PdfColor.FromSeparation("PMS300", 0.8))
            .FillOpacity(0.6)
            .Translate(200, 600)
            .Rotate(30)
            .WithBlendMode("Multiply");

        // Another layer with different transform
        page.BeginPath()
            .Circle(0, 0, 40)
            .Fill(PdfColor.FromSeparation("PMS375", 0.9))
            .FillOpacity(0.5)
            .Translate(200, 600)
            .Scale(1.5, 0.7)
            .WithBlendMode("Screen");

        // Note at bottom
        page.AddText("Note: View in Adobe Acrobat or Ghostscript for accurate rendering", 50, 100, "Helvetica", 10)
            .Fill(PdfColor.DarkGray);
    }
}
