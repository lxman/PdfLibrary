using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;

// Output directory for generated PDFs
const string outputDir = @"C:\Users\jorda\RiderProjects\PDF\PdfLibrary.Examples\TestPdfs";
Directory.CreateDirectory(outputDir);

Console.WriteLine("PDF Generator - Creating showcase PDFs...\n");

// Generate company logo
Console.WriteLine("Generating company logo...");
Generator.LogoGenerator.GenerateCompanyLogo(Path.Combine(outputDir, "company-logo.jpg"));
Console.WriteLine("  ✓ Created company-logo.jpg\n");

// ==================== SHOWCASE.PDF ====================
Console.WriteLine("Creating showcase.pdf...");
GenerateShowcasePdf(Path.Combine(outputDir, "showcase.pdf"));
Console.WriteLine("  ✓ Created showcase.pdf\n");

Console.WriteLine("All PDFs generated successfully!");
Console.WriteLine($"Output directory: {outputDir}");

static void GenerateShowcasePdf(string outputPath)
{
    PdfDocumentBuilder.Create()
        .WithMetadata(m => m
            .SetTitle("PdfLibrary Showcase")
            .SetAuthor("PdfLibrary")
            .SetSubject("Demonstration of PdfLibrary rendering capabilities")
            .SetKeywords("pdf, rendering, graphics, text"))

        // ==================== PAGE 1: Text & Colors ====================
        .AddPage(p =>
        {
            p.FromTopLeft(); // Use top-left origin for easier positioning

            // Title
            p.AddText("PdfLibrary Showcase", 72, 50)
                .Font("Helvetica-Bold", 36)
                .WithColor(PdfColor.FromHex("#000080")); // Navy

            // Subtitle
            p.AddText("Demonstrating Rendering Capabilities", 72, 100)
                .Font("Helvetica", 18)
                .WithColor(PdfColor.DarkGray);

            // Colored rectangles to test color accuracy
            p.AddText("Color Rendering Test", 72, 140)
                .Font("Helvetica-Bold", 14);

            p.AddRectangle(72, 160, 100, 60, PdfColor.Red);
            p.AddText("Red", 95, 195)
                .Font("Helvetica", 12)
                .WithColor(PdfColor.White);

            p.AddRectangle(180, 160, 100, 60, PdfColor.Green);
            p.AddText("Green", 200, 195)
                .Font("Helvetica", 12)
                .WithColor(PdfColor.White);

            p.AddRectangle(288, 160, 100, 60, PdfColor.Blue);
            p.AddText("Blue", 310, 195)
                .Font("Helvetica", 12)
                .WithColor(PdfColor.White);

            p.AddRectangle(396, 160, 100, 60, PdfColor.Yellow);
            p.AddText("Yellow", 415, 195)
                .Font("Helvetica", 12)
                .WithColor(PdfColor.Black);

            // Feature list
            p.AddText("Key Features:", 72, 250)
                .Font("Helvetica-Bold", 14);

            p.AddText("\u2022 High-quality PDF rendering with SkiaSharp", 90, 275)
                .Font("Helvetica", 11);

            p.AddText("\u2022 Support for all PDF 1.x and 2.0 features", 90, 295)
                .Font("Helvetica", 11);

            p.AddText("\u2022 Accurate color space handling", 90, 315)
                .Font("Helvetica", 11);

            p.AddText("\u2022 Comprehensive font support", 90, 335)
                .Font("Helvetica", 11);

            p.AddText("\u2022 Multiple image compression formats", 90, 355)
                .Font("Helvetica", 11);

            p.AddText("\u2022 Full graphics state management", 90, 375)
                .Font("Helvetica", 11);

            // Shapes section
            p.AddText("Shape Rendering Test", 72, 420)
                .Font("Helvetica-Bold", 14);

            // Circles (using bottom-left coordinates: 792 - 480 = 312)
            p.AddCircle(122, 312, 40)
                .Fill(PdfColor.FromRgb(255, 100, 100));

            p.AddCircle(230, 312, 40)
                .Fill(PdfColor.FromRgb(100, 255, 100));

            p.AddCircle(338, 312, 40)
                .Fill(PdfColor.FromRgb(100, 100, 255));

            p.AddCircle(446, 312, 40)
                .Stroke(PdfColor.Black, 2);

            // Rectangles with different styles
            p.AddRectangle(72, 580, 80, 60, PdfColor.FromHex("#FFA500")); // Orange

            p.AddRectangle(180, 580, 80, 60, null, PdfColor.FromHex("#800080"), 3); // Purple outline only

            p.AddRectangle(288, 580, 80, 60, PdfColor.Cyan, PdfColor.Magenta, 2);

            // Footer
            p.AddText("Generated with PdfLibrary v0.0.10-beta", 72, 720)
                .Font("Helvetica", 10)
                .WithColor(PdfColor.LightGray);

            p.AddText("https://github.com/lxman/PdfLibrary", 72, 735)
                .Font("Helvetica", 10)
                .WithColor(PdfColor.Blue);
        })

        .Save(outputPath);
}
