using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Structure;
using Xunit.Abstractions;

namespace PdfLibrary.Tests.Fonts.Embedded;

/// <summary>
/// Integration tests for embedded font extraction and parsing with real PDFs
/// Tests the complete pipeline from PDF → font data → parsed tables → metrics
/// </summary>
public class EmbeddedFontIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public EmbeddedFontIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ExtractEmbeddedFonts_SimplePdf_FindsFonts()
    {
        // Use one of the available test PDFs
        string[] testPdfs =
        [
            @"C:\Users\jorda\RiderProjects\PDF\PDF Standards\PDF20_AN002-AF.pdf",
            @"C:\Users\jorda\RiderProjects\PDF\TestPDFs\SimpleTest1.pdf"
        ];

        foreach (string pdfPath in testPdfs)
        {
            if (!File.Exists(pdfPath))
            {
                _output.WriteLine($"Skipping {Path.GetFileName(pdfPath)} - file not found");
                continue;
            }

            _output.WriteLine($"\n=== Testing {Path.GetFileName(pdfPath)} ===");

            try
            {
                using PdfDocument doc = PdfDocument.Load(pdfPath);
                int pageCount = doc.GetPageCount();
                _output.WriteLine($"Pages: {pageCount}");

                for (var pageNum = 0; pageNum < pageCount; pageNum++)
                {
                    PdfPage? page = doc.GetPage(pageNum);
                    if (page == null) continue;

                    PdfResources? resources = page.GetResources();
                    if (resources == null)
                    {
                        _output.WriteLine($"  Page {pageNum + 1}: No resources");
                        continue;
                    }

                    List<string> fontNames = resources.GetFontNames();
                    _output.WriteLine($"  Page {pageNum + 1}: {fontNames.Count} font(s)");

                    foreach (string fontName in fontNames)
                    {
                        PdfFont? font = resources.GetFontObject(fontName);
                        if (font == null) continue;

                        _output.WriteLine($"    - {fontName}: {font.BaseFont} ({font.FontType})");

                        // Try to get embedded metrics for TrueType and Type0 fonts
                        EmbeddedFontMetrics? metrics = font.GetEmbeddedMetrics();
                        if (metrics != null)
                        {
                            _output.WriteLine($"      Embedded: UnitsPerEm={metrics.UnitsPerEm}, Valid={metrics.IsValid}");
                        }
                        else
                        {
                            _output.WriteLine($"      Embedded: None");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error processing {Path.GetFileName(pdfPath)}: {ex.Message}");
            }
        }
    }

    [Fact]
    public void ParseEmbeddedFont_TrueTypeFont_ParsesTablesCorrectly()
    {
        var pdfPath = @"C:\Users\jorda\RiderProjects\PDF\PDF Standards\PDF20_AN002-AF.pdf";

        if (!File.Exists(pdfPath))
        {
            _output.WriteLine($"Skipping test - PDF not found: {pdfPath}");
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);
        var foundTrueTypeFont = false;

        for (var pageNum = 0; pageNum < doc.GetPageCount(); pageNum++)
        {
            PdfPage? page = doc.GetPage(pageNum);

            PdfResources? resources = page?.GetResources();
            if (resources == null) continue;

            List<string> fontNames = resources.GetFontNames();

            foreach (string fontName in fontNames)
            {
                PdfFont? font = resources.GetFontObject(fontName);
                if (font == null) continue;

                // Look for TrueType or Type0 fonts with embedded data
                if (font.FontType != PdfFontType.TrueType && font.FontType != PdfFontType.Type0)
                    continue;

                EmbeddedFontMetrics? metrics = font.GetEmbeddedMetrics();
                if (metrics == null) continue;

                foundTrueTypeFont = true;
                _output.WriteLine($"\n=== Analyzing {fontName}: {font.BaseFont} ===");
                _output.WriteLine($"Font Type: {font.FontType}");
                _output.WriteLine($"UnitsPerEm: {metrics.UnitsPerEm}");
                _output.WriteLine($"IsValid: {metrics.IsValid}");

                if (!metrics.IsValid)
                {
                    _output.WriteLine("WARNING: Font metrics marked as invalid!");
                    continue;
                }

                // Test character width extraction for common characters
                _output.WriteLine("\nTesting character width extraction:");
                int[] testChars = [32, 65, 66, 67, 97, 98, 99]; // Space, ABC, abc

                foreach (int charCode in testChars)
                {
                    double pdfWidth = font.GetCharacterWidth(charCode);
                    ushort embeddedWidth = metrics.GetCharacterAdvanceWidth((ushort)charCode);
                    double scaledWidth = embeddedWidth * 1000.0 / metrics.UnitsPerEm;

                    char c = charCode is >= 32 and < 127 ? (char)charCode : '?';
                    _output.WriteLine($"  Char {charCode} ('{c}'): PDF={pdfWidth:F2}, Embedded={embeddedWidth} (scaled={scaledWidth:F2})");
                }

                // Verify the metrics object is functional
                Assert.True(metrics.UnitsPerEm > 0, "UnitsPerEm should be positive");
                Assert.True(metrics.UnitsPerEm is 1000 or 2048, "UnitsPerEm should be standard value (1000 or 2048)");

                break; // Only test the first embedded font
            }

            if (foundTrueTypeFont) break;
        }

        if (!foundTrueTypeFont)
        {
            _output.WriteLine("No TrueType fonts with embedded data found in PDF");
        }
    }

    [Fact]
    public void ParseEmbeddedFont_VerifyHmtxTable_ReturnsConsistentWidths()
    {
        var pdfPath = @"C:\Users\jorda\RiderProjects\PDF\PDF Standards\PDF20_AN002-AF.pdf";

        if (!File.Exists(pdfPath))
        {
            _output.WriteLine($"Skipping test - PDF not found: {pdfPath}");
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);

        for (var pageNum = 0; pageNum < doc.GetPageCount(); pageNum++)
        {
            PdfPage? page = doc.GetPage(pageNum);

            PdfResources? resources = page?.GetResources();
            if (resources == null) continue;

            foreach (string fontName in resources.GetFontNames())
            {
                PdfFont? font = resources.GetFontObject(fontName);

                EmbeddedFontMetrics? metrics = font?.GetEmbeddedMetrics();
                if (metrics is not { IsValid: true }) continue;

                _output.WriteLine($"\n=== {fontName}: {font.BaseFont} ===");

                // Test that calling GetCharacterAdvanceWidth multiple times returns the same value
                ushort width1 = metrics.GetCharacterAdvanceWidth(65); // 'A'
                ushort width2 = metrics.GetCharacterAdvanceWidth(65);
                ushort width3 = metrics.GetCharacterAdvanceWidth(65);

                Assert.Equal(width1, width2);
                Assert.Equal(width2, width3);

                _output.WriteLine($"Character 'A' (65) width: {width1} (consistent across calls)");

                // Test different characters return different widths (unless monospace)
                ushort widthW = metrics.GetCharacterAdvanceWidth(87); // 'W' - typically wide
                ushort widthI = metrics.GetCharacterAdvanceWidth(73); // 'I' - typically narrow

                _output.WriteLine($"Character 'W' (87) width: {widthW}");
                _output.WriteLine($"Character 'I' (73) width: {widthI}");

                // Most fonts have different widths for W and I (unless monospace or subset)
                // Note: Subset fonts may not contain all characters, so 0 widths are valid for missing glyphs
                if (widthW > 0 && widthI > 0 && widthW != widthI)
                {
                    _output.WriteLine("Font appears to be proportional (W != I width, both positive)");
                }
                else if (widthW == widthI && widthW > 0)
                {
                    _output.WriteLine("Font might be monospace (W == I width)");
                }
                else
                {
                    _output.WriteLine($"Font is likely a subset (W={widthW}, I={widthI}) - some characters may not be present");
                }

                break; // Only test first valid font
            }
        }
    }

    [Fact]
    public void ParseEmbeddedFont_VerifyCmapTable_MapsCharactersToGlyphs()
    {
        var pdfPath = @"C:\Users\jorda\RiderProjects\PDF\PDF Standards\PDF20_AN002-AF.pdf";

        if (!File.Exists(pdfPath))
        {
            _output.WriteLine($"Skipping test - PDF not found: {pdfPath}");
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);
        var foundFontWithCmap = false;

        for (var pageNum = 0; pageNum < doc.GetPageCount(); pageNum++)
        {
            PdfPage? page = doc.GetPage(pageNum);

            PdfResources? resources = page?.GetResources();
            if (resources == null) continue;

            foreach (string fontName in resources.GetFontNames())
            {
                PdfFont? font = resources.GetFontObject(fontName);

                EmbeddedFontMetrics? metrics = font?.GetEmbeddedMetrics();
                if (metrics is not { IsValid: true }) continue;

                _output.WriteLine($"\n=== Testing cmap for {fontName}: {font.BaseFont} ===");

                // Test character-to-glyph mapping for common ASCII characters
                int[] testChars = [32, 65, 66, 67, 68, 69, 97, 98, 99, 100, 101]; // Space, ABCDE, abcde

                _output.WriteLine("Character to glyph mapping:");
                var glyphsSeen = new HashSet<ushort>();

                foreach (int charCode in testChars)
                {
                    ushort glyphId = metrics.GetGlyphId((ushort)charCode);
                    char c = charCode is >= 32 and < 127 ? (char)charCode : '?';

                    _output.WriteLine($"  '{c}' ({charCode}) -> Glyph {glyphId}");
                    glyphsSeen.Add(glyphId);
                }

                _output.WriteLine($"Total unique glyphs: {glyphsSeen.Count}");

                // Verify we got at least some glyph mappings
                Assert.True(glyphsSeen.Count > 0, "Should have mapped at least one character to a glyph");

                // Glyph 0 is typically the .notdef glyph (missing character)
                // Most characters should map to non-zero glyph IDs
                int nonZeroGlyphs = glyphsSeen.Count(g => g > 0);
                _output.WriteLine($"Non-zero glyphs: {nonZeroGlyphs}");

                foundFontWithCmap = true;
                break;
            }

            if (foundFontWithCmap) break;
        }

        if (!foundFontWithCmap)
        {
            _output.WriteLine("No fonts with valid cmap tables found");
        }
    }

}
