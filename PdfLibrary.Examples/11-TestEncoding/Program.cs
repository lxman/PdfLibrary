using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;

// Test Unicode → WinAnsi encoding implementation
const string outputPath = @"C:\Users\jorda\RiderProjects\PDF\PdfLibrary.Examples\TestPdfs\test_encoding.pdf";

Console.WriteLine("Testing Unicode → WinAnsi Encoding\n");
Console.WriteLine($"Creating test PDF: {outputPath}\n");

PdfDocumentBuilder.Create()
    .WithMetadata(m => m
        .SetTitle("WinAnsi Encoding Test")
        .SetAuthor("PdfLibrary")
        .SetSubject("Testing special character encoding"))

    .AddPage(p =>
    {
        p.FromTopLeft();

        p.AddText("WINANSENCODING TEST", 72, 50)
            .Font("Helvetica-Bold", 24)
            .WithColor(PdfColor.FromHex("#2C3E50"));

        p.AddRectangle(72, 80, 468, 2, PdfColor.FromHex("#2C3E50"));

        p.AddText("Testing special characters that should now work:", 72, 110)
            .Font("Helvetica-Bold", 14);

        double y = 140;
        var tests = new[]
        {
            ("Degree symbol", "Temperature: 72\u00B0F", "U+00B0 -> byte 176"),
            ("Bullet", "Features: \u2022 Easy to use \u2022 Fast \u2022 Reliable", "U+2022 -> byte 149"),
            ("Trademark", "PdfLibrary\u2122", "U+2122 -> byte 153"),
            ("Copyright", "Copyright \u00A9 2024", "U+00A9 -> byte 169"),
            ("Registered", "Adobe\u00AE Reader\u00AE", "U+00AE -> byte 174"),
            ("En dash", "Pages 10\u201320", "U+2013 -> byte 150"),
            ("Em dash", "PdfLibrary\u2014the best choice", "U+2014 -> byte 151"),
            ("Smart quotes", "\u201CHello World\u201D", "U+201C/D -> bytes 147/148"),
            ("Euro", "Price: \u20AC50", "U+20AC -> byte 128"),
            ("Ellipsis", "Loading\u2026", "U+2026 -> byte 133"),
        };

        foreach (var (label, text, mapping) in tests)
        {
            p.AddText($"{label}:", 72, y)
                .Font("Helvetica-Bold", 11)
                .WithColor(PdfColor.FromHex("#34495E"));

            p.AddText(text, 200, y)
                .Font("Helvetica", 11);

            p.AddText(mapping, 400, y)
                .Font("Helvetica", 8)
                .WithColor(PdfColor.DarkGray);

            y += 20;
        }

        // Test multiple fonts
        y += 30;
        p.AddText("Testing across different fonts:", 72, y)
            .Font("Helvetica-Bold", 14);

        y += 30;
        var fonts = new[] { "Helvetica", "Helvetica-Bold", "Times-Roman", "Courier" };
        foreach (var font in fonts)
        {
            p.AddText($"{font}: 72\u00B0F \u2022 Product\u2122 \u00A9 2024", 72, y)
                .Font(font, 11);
            y += 18;
        }

        // Status
        y += 30;
        p.AddRectangle(72, y, 468, 60, PdfColor.FromHex("#ECF0F1"));
        p.AddText("If all characters above display correctly (not as '?'),", 82, y + 15)
            .Font("Helvetica-Bold", 11)
            .WithColor(PdfColor.FromHex("#27AE60"));
        p.AddText("then the Unicode → WinAnsi encoding is working!", 82, y + 35)
            .Font("Helvetica-Bold", 11)
            .WithColor(PdfColor.FromHex("#27AE60"));
    })

    .Save(outputPath);

Console.WriteLine("✓ Test PDF created successfully!\n");
Console.WriteLine($"File: {outputPath}\n");
Console.WriteLine("Please open the PDF and verify that all special characters");
Console.WriteLine("render correctly instead of appearing as '?'.\n");
Console.WriteLine("Characters to verify:");
Console.WriteLine("  \u00B0 (degree) - Should show in '72\u00B0F'");
Console.WriteLine("  \u2022 (bullet) - Should show in feature lists");
Console.WriteLine("  \u2122 (trademark) - Should show in 'Product\u2122'");
Console.WriteLine("  \u00A9 (copyright) - Should show in '\u00A9 2024'");
Console.WriteLine("  \u00AE (registered) - Should show in 'Adobe\u00AE'");
Console.WriteLine("  \u2013 (en dash) - Should show in 'Pages 10\u201320'");
Console.WriteLine("  \u2014 (em dash) - Should show in 'PdfLibrary\u2014the'");
Console.WriteLine("  \u201C \u201D (smart quotes) - Should show in '\u201CHello World\u201D'");
Console.WriteLine("  \u20AC (euro) - Should show in '\u20AC50'");
Console.WriteLine("  \u2026 (ellipsis) - Should show in 'Loading\u2026'\n");
Console.WriteLine("Done!");
