using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;
using PdfLibrary.Structure;

// ==================== WinAnsi encoding example ====================
// The standard-14 fonts (Helvetica, Times, Courier) are encoded with the WinAnsi code page.
// This shows the builder mapping non-ASCII Unicode text — degree, bullet, trademark, copyright,
// dashes, smart quotes, euro, ellipsis — to their correct single-byte WinAnsi codes so they
// render, instead of dropping to '?'. It then reads the text back with ExtractText() to confirm
// the characters survive the round-trip.
//
// Usage:
//   dotnet run                 # writes to a temp file
//   dotnet run -- <out.pdf>    # writes to a chosen path

Console.WriteLine("PdfLibrary — WinAnsi Encoding Example\n");

string outputPath = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.Combine(Path.GetTempPath(), "pdflibrary-examples", "test_encoding.pdf");
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

Console.WriteLine($"Creating test PDF: {outputPath}\n");

(string Label, string Text, string Mapping)[] tests =
[
    ("Degree symbol", "Temperature: 72°F", "U+00B0 -> byte 176"),
    ("Bullet", "Features: • Easy to use • Fast • Reliable", "U+2022 -> byte 149"),
    ("Trademark", "PdfLibrary™", "U+2122 -> byte 153"),
    ("Copyright", "Copyright © 2024", "U+00A9 -> byte 169"),
    ("Registered", "Adobe® Reader®", "U+00AE -> byte 174"),
    ("En dash", "Pages 10–20", "U+2013 -> byte 150"),
    ("Em dash", "PdfLibrary—the best choice", "U+2014 -> byte 151"),
    ("Smart quotes", "“Hello World”", "U+201C/D -> bytes 147/148"),
    ("Euro", "Price: €50", "U+20AC -> byte 128"),
    ("Ellipsis", "Loading…", "U+2026 -> byte 133"),
];

PdfDocumentBuilder.Create()
    .WithMetadata(m => m
        .SetTitle("WinAnsi Encoding Test")
        .SetAuthor("PdfLibrary")
        .SetSubject("Testing special character encoding"))

    .AddPage(p =>
    {
        p.FromTopLeft();

        p.AddText("WINANSI ENCODING TEST", 72, 50)
            .Font("Helvetica-Bold", 24)
            .WithColor(PdfColor.FromHex("#2C3E50"));

        p.AddRectangle(72, 80, 468, 2, PdfColor.FromHex("#2C3E50"));

        p.AddText("Testing special characters that should now work:", 72, 110)
            .Font("Helvetica-Bold", 14);

        double y = 140;
        foreach ((string label, string text, string mapping) in tests)
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
        foreach (string font in fonts)
        {
            p.AddText($"{font}: 72°F • Product™ © 2024", 72, y)
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

Console.WriteLine("✓ Test PDF created.\n");

// ---- Read the text back to confirm the characters survived the WinAnsi round-trip ----
using PdfDocument doc = PdfDocument.Load(outputPath);
string extracted = doc.GetPage(0)!.ExtractText();

Console.WriteLine("Round-trip check (character found in extracted text):");
foreach ((string Label, string Text, string Mapping) t in tests)
{
    // The distinctive non-ASCII character of each test string.
    char special = t.Text.First(c => c > 0x7F);
    Console.WriteLine($"  {(extracted.Contains(special) ? "✓" : "·")} {t.Label} ('{special}')");
}

Console.WriteLine($"\nFile: {outputPath}");
Console.WriteLine("Open it in a viewer to confirm the characters render (not as '?').");
Console.WriteLine("\nDone!");
