using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;

// ==================== CONFIGURATION ====================
const string outputPath = @"C:\Users\jorda\RiderProjects\PDF\PdfLibrary.Examples\TestPdfs\comprehensive.pdf";

Console.WriteLine("Comprehensive PDF Builder Showcase\n");
Console.WriteLine($"Creating document: {outputPath}\n");
Console.WriteLine("This example demonstrates:\n");
Console.WriteLine("  - Multiple pages with different layouts");
Console.WriteLine("  - Text with various fonts, sizes, colors, and rotation");
Console.WriteLine("  - Geometric shapes and graphics");
Console.WriteLine("  - Tables and structured content");
Console.WriteLine("  - Bookmarks for navigation");
Console.WriteLine("  - Metadata and document properties\n");

// ==================== CREATE COMPREHENSIVE PDF ====================
PdfDocumentBuilder.Create()
    .WithMetadata(m => m
        .SetTitle("PdfLibrary Builder API Showcase")
        .SetAuthor("PdfLibrary Examples")
        .SetSubject("Comprehensive demonstration of PDF creation capabilities")
        .SetKeywords("PDF, C#, Builder API, Graphics, Typography")
        .SetCreator("PdfLibrary.Examples.CreateFromScratch")
        .SetProducer("PdfLibrary"))

    // Bookmarks
    .AddBookmark("Cover Page", b => b.ToPage(0).FitPage().Bold().WithColor(PdfColor.FromHex("#2C3E50")))
    .AddBookmark("Typography Showcase", b => b.ToPage(1).FitPage().Bold().WithColor(PdfColor.FromHex("#3498DB")))
    .AddBookmark("Color & Graphics", b => b.ToPage(2).FitPage().Bold().WithColor(PdfColor.FromHex("#E74C3C")))
    .AddBookmark("Shapes & Patterns", b => b.ToPage(3).FitPage().Bold().WithColor(PdfColor.FromHex("#27AE60")))
    .AddBookmark("Document Information", b => b.ToPage(4).FitPage().Bold().WithColor(PdfColor.FromHex("#95A5A6")))

    // ==================== PAGE 1: COVER PAGE ====================
    .AddPage(p =>
    {
        p.FromTopLeft();

        // Title with shadow effect (using layered text)
        p.AddText("PdfLibrary", 74, 202)
            .Font("Helvetica-Bold", 72)
            .WithColor(PdfColor.FromRgb(200, 200, 200));  // Shadow

        p.AddText("PdfLibrary", 72, 200)
            .Font("Helvetica-Bold", 72)
            .WithColor(PdfColor.FromHex("#2C3E50"));

        p.AddText("Builder API Showcase", 72, 280)
            .Font("Helvetica", 32)
            .WithColor(PdfColor.FromHex("#7F8C8D"));

        // Decorative line
        p.AddRectangle(72, 330, 468, 4, PdfColor.FromHex("#3498DB"));

        // Version info
        p.AddText("Version 1.0", 72, 360)
            .Font("Helvetica-Oblique", 14)
            .WithColor(PdfColor.DarkGray);

        // Feature grid (colored boxes with labels)
        var featureY = 420;
        var boxSize = 50;
        var spacing = 60;

        var features = new[]
        {
            ("Text", PdfColor.FromHex("#E74C3C")),
            ("Shapes", PdfColor.FromHex("#3498DB")),
            ("Colors", PdfColor.FromHex("#27AE60")),
            ("Fonts", PdfColor.FromHex("#F39C12")),
            ("Layout", PdfColor.FromHex("#9B59B6")),
            ("Tables", PdfColor.FromHex("#1ABC9C"))
        };

        for (int i = 0; i < features.Length; i++)
        {
            var row = i / 3;
            var col = i % 3;
            var x = 72 + (col * (boxSize + spacing + 80));
            var y = featureY + (row * (boxSize + 40));

            // Colored box
            p.AddRectangle(x, y, boxSize, boxSize, features[i].Item2);

            // Label
            p.AddText(features[i].Item1, x + boxSize + 10, y + 18)
                .Font("Helvetica-Bold", 14)
                .WithColor(features[i].Item2);
        }

        // Footer
        p.AddRectangle(72, 720, 468, 1, PdfColor.LightGray);
        p.AddText("Created with PdfLibrary - A C# PDF Creation Library", 72, 740)
            .Font("Helvetica-Oblique", 10)
            .WithColor(PdfColor.DarkGray);
    })

    // ==================== PAGE 2: TYPOGRAPHY SHOWCASE ====================
    .AddPage(p =>
    {
        p.FromTopLeft();

        p.AddText("TYPOGRAPHY SHOWCASE", 72, 50)
            .Font("Helvetica-Bold", 32)
            .WithColor(PdfColor.FromHex("#3498DB"));

        p.AddRectangle(72, 90, 468, 2, PdfColor.FromHex("#3498DB"));

        // Font families
        var y = 120.0;
        p.AddText("Font Families:", 72, y)
            .Font("Helvetica-Bold", 16);

        y += 30;
        var fonts = new[]
        {
            ("Helvetica", "The quick brown fox jumps over the lazy dog"),
            ("Helvetica-Bold", "The quick brown fox jumps over the lazy dog"),
            ("Helvetica-Oblique", "The quick brown fox jumps over the lazy dog"),
            ("Helvetica-BoldOblique", "The quick brown fox jumps over the lazy dog"),
            ("Times-Roman", "The quick brown fox jumps over the lazy dog"),
            ("Times-Bold", "The quick brown fox jumps over the lazy dog"),
            ("Courier", "The quick brown fox jumps over the lazy dog")
        };

        foreach (var (font, text) in fonts)
        {
            p.AddText($"{font}:", 72, y)
                .Font("Helvetica", 9)
                .WithColor(PdfColor.DarkGray);

            p.AddText(text, 200, y)
                .Font(font, 11);

            y += 20;
        }

        // Text sizes
        y += 20;
        p.AddText("Text Sizes:", 72, y)
            .Font("Helvetica-Bold", 16);

        y += 30;
        var sizes = new[] { 8, 10, 12, 14, 18, 24, 32 };
        foreach (var size in sizes)
        {
            p.AddText($"Size {size}pt", 72, y)
                .Font("Helvetica", size);
            y += size + 6;
        }

        // Text rotation
        y += 20;
        p.AddText("Text Rotation:", 72, y)
            .Font("Helvetica-Bold", 16);

        y += 50;
        p.AddText("0\u00B0", 150, y).Font("Helvetica", 14);
        p.AddText("45\u00B0", 250, y).Font("Helvetica", 14).Rotate(45);
        p.AddText("90\u00B0", 350, y).Font("Helvetica", 14).Rotate(90);
        p.AddText("-45\u00B0", 450, y).Font("Helvetica", 14).Rotate(-45);
    })

    // ==================== PAGE 3: COLOR & GRAPHICS ====================
    .AddPage(p =>
    {
        p.FromTopLeft();

        p.AddText("COLOR & GRAPHICS", 72, 50)
            .Font("Helvetica-Bold", 32)
            .WithColor(PdfColor.FromHex("#E74C3C"));

        p.AddRectangle(72, 90, 468, 2, PdfColor.FromHex("#E74C3C"));

        // Color spectrum
        p.AddText("Color Spectrum (RGB):", 72, 120)
            .Font("Helvetica-Bold", 16);

        var colorY = 150;
        var colorHeight = 30;
        var colors = new[]
        {
            ("Red", PdfColor.Red),
            ("Green", PdfColor.Green),
            ("Blue", PdfColor.Blue),
            ("Cyan", PdfColor.Cyan),
            ("Magenta", PdfColor.Magenta),
            ("Yellow", PdfColor.Yellow),
            ("Black", PdfColor.Black),
            ("Gray", PdfColor.FromRgb(128, 128, 128)),
            ("Light Gray", PdfColor.LightGray),
            ("Dark Gray", PdfColor.DarkGray)
        };

        foreach (var (name, color) in colors)
        {
            p.AddRectangle(72, colorY, 200, colorHeight, color);
            var textColor = name == "Black" || name == "Dark Gray" || name == "Blue" || name == "Magenta"
                ? PdfColor.White
                : PdfColor.Black;
            p.AddText(name, 82, colorY + 9)
                .Font("Helvetica-Bold", 12)
                .WithColor(textColor);

            colorY += colorHeight + 2;
        }

        // Custom colors
        p.AddText("Custom Colors (Hex):", 320, 120)
            .Font("Helvetica-Bold", 16);

        colorY = 150;
        var customColors = new[]
        {
            ("#2C3E50", "Midnight Blue"),
            ("#3498DB", "Peter River"),
            ("#1ABC9C", "Turquoise"),
            ("#27AE60", "Nephritis"),
            ("#F39C12", "Orange"),
            ("#E74C3C", "Alizarin")
        };

        foreach (var (hex, name) in customColors)
        {
            p.AddRectangle(320, colorY, 200, colorHeight, PdfColor.FromHex(hex));
            p.AddText($"{name} ({hex})", 330, colorY + 9)
                .Font("Helvetica-Bold", 11)
                .WithColor(PdfColor.White);

            colorY += colorHeight + 2;
        }

        // Gradient simulation (using multiple rectangles)
        p.AddText("Gradient Effect (simulated with rectangles):", 72, 480)
            .Font("Helvetica-Bold", 14);

        var gradientY = 510;
        for (int i = 0; i < 20; i++)
        {
            var gray = (byte)(255 - (i * 12));
            p.AddRectangle(72 + (i * 23), gradientY, 23, 40, PdfColor.FromRgb(gray, gray, gray));
        }
    })

    // ==================== PAGE 4: SHAPES & PATTERNS ====================
    .AddPage(p =>
    {
        p.FromTopLeft();

        p.AddText("SHAPES & PATTERNS", 72, 50)
            .Font("Helvetica-Bold", 32)
            .WithColor(PdfColor.FromHex("#27AE60"));

        p.AddRectangle(72, 90, 468, 2, PdfColor.FromHex("#27AE60"));

        // Rectangles
        p.AddText("Rectangles:", 72, 120)
            .Font("Helvetica-Bold", 16);

        p.AddRectangle(72, 150, 100, 60, PdfColor.FromHex("#E74C3C"));
        p.AddText("Filled", 72, 220).Font("Helvetica", 10);

        p.AddRectangle(200, 150, 100, 60, PdfColor.FromHex("#3498DB"));
        p.AddText("Square", 200, 220).Font("Helvetica", 10);

        p.AddRectangle(328, 150, 120, 40, PdfColor.FromHex("#F39C12"));
        p.AddText("Wide", 328, 220).Font("Helvetica", 10);

        // Lines (using thin rectangles)
        p.AddText("Lines:", 72, 260)
            .Font("Helvetica-Bold", 16);

        p.AddRectangle(72, 290, 200, 1, PdfColor.Black);
        p.AddText("Horizontal", 72, 300).Font("Helvetica", 10);

        p.AddRectangle(300, 290, 200, 3, PdfColor.FromHex("#E74C3C"));
        p.AddText("Thick Line", 300, 300).Font("Helvetica", 10);

        p.AddRectangle(72, 320, 468, 1, PdfColor.FromRgb(128, 128, 128));
        p.AddText("Full Width", 72, 330).Font("Helvetica", 10);

        // Pattern simulation
        p.AddText("Patterns (grid):", 72, 370)
            .Font("Helvetica-Bold", 16);

        var gridSize = 10;
        var gridStartY = 400;
        for (int row = 0; row < 10; row++)
        {
            for (int col = 0; col < 20; col++)
            {
                var color = (row + col) % 2 == 0
                    ? PdfColor.FromHex("#3498DB")
                    : PdfColor.FromHex("#ECF0F1");
                p.AddRectangle(72 + (col * gridSize), gridStartY + (row * gridSize),
                    gridSize, gridSize, color);
            }
        }

        // Decorative border
        p.AddRectangle(70, 540, 472, 104, PdfColor.FromHex("#34495E"));
        p.AddRectangle(72, 542, 468, 100, PdfColor.White);
        p.AddText("Nested Rectangles Create Borders", 150, 575)
            .Font("Helvetica-Bold", 18)
            .WithColor(PdfColor.FromHex("#34495E"));
    })

    // ==================== PAGE 5: DOCUMENT INFORMATION ====================
    .AddPage(p =>
    {
        p.FromTopLeft();

        p.AddText("DOCUMENT INFORMATION", 72, 50)
            .Font("Helvetica-Bold", 32)
            .WithColor(PdfColor.FromHex("#95A5A6"));

        p.AddRectangle(72, 90, 468, 2, PdfColor.FromHex("#95A5A6"));

        // Metadata info
        p.AddText("This PDF was created programmatically using PdfLibrary.", 72, 120)
            .Font("Helvetica", 12);

        var y = 160.0;
        var infoItems = new[]
        {
            ("Title", "PdfLibrary Builder API Showcase"),
            ("Author", "PdfLibrary Examples"),
            ("Subject", "Comprehensive demonstration of PDF creation capabilities"),
            ("Keywords", "PDF, C#, Builder API, Graphics, Typography"),
            ("Creator", "PdfLibrary.Examples.CreateFromScratch"),
            ("Producer", "PdfLibrary")
        };

        p.AddText("Metadata:", 72, y)
            .Font("Helvetica-Bold", 14);
        y += 25;

        foreach (var (key, value) in infoItems)
        {
            p.AddText($"{key}:", 72, y)
                .Font("Helvetica-Bold", 11)
                .WithColor(PdfColor.FromHex("#34495E"));

            p.AddText(value, 180, y)
                .Font("Helvetica", 11);

            y += 18;
        }

        // Features demonstrated
        y += 20;
        p.AddText("Features Demonstrated:", 72, y)
            .Font("Helvetica-Bold", 14);
        y += 25;

        var features = new[]
        {
            "\u2022 Multiple pages with different content",
            "\u2022 Text with various fonts (Helvetica, Times, Courier)",
            "\u2022 Text styling (bold, italic, different sizes)",
            "\u2022 Text rotation (0\u00B0, 45\u00B0, 90\u00B0, -45\u00B0)",
            "\u2022 Colors (predefined, RGB, hex)",
            "\u2022 Shapes (rectangles, lines)",
            "\u2022 Patterns and gradients (simulated)",
            "\u2022 Bookmarks for navigation",
            "\u2022 Document metadata",
            "\u2022 Layered graphics (shadow effects)",
            "\u2022 Borders and decorative elements"
        };

        foreach (var feature in features)
        {
            p.AddText(feature, 90, y)
                .Font("Helvetica", 11);
            y += 16;
        }

        // Footer
        y = 720;
        p.AddRectangle(72, y, 468, 1, PdfColor.LightGray);
        p.AddText("End of Document", 72, y + 15)
            .Font("Helvetica-Oblique", 10)
            .WithColor(PdfColor.DarkGray);

        var today = DateTime.Now.ToString("MMMM dd, yyyy");
        p.AddText($"Generated on {today}", 370, y + 15)
            .Font("Helvetica-Oblique", 10)
            .WithColor(PdfColor.DarkGray);
    })

    .Save(outputPath);

Console.WriteLine($"\n✓ Document created successfully!\n");
Console.WriteLine($"File: {outputPath}\n");
Console.WriteLine("Document Structure:");
Console.WriteLine("  Page 1 - Cover Page with title and feature grid");
Console.WriteLine("  Page 2 - Typography (fonts, sizes, rotation)");
Console.WriteLine("  Page 3 - Colors (RGB, hex, custom palettes)");
Console.WriteLine("  Page 4 - Shapes and patterns");
Console.WriteLine("  Page 5 - Document information and features list\n");
Console.WriteLine("Open the PDF to explore all features demonstrated!\n");
Console.WriteLine("Done!");