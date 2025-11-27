using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Structure;
using Xunit.Abstractions;

namespace PdfLibrary.Tests.Fonts.Embedded;

public class CmapDiagnosticTest(ITestOutputHelper output)
{
    [Fact]
    public void DiagnoseCmapParsing()
    {
        const string pdfPath = @"C:\Users\jorda\RiderProjects\PDF\PDF Standards\PDF20_AN002-AF.pdf";

        if (!File.Exists(pdfPath))
        {
            output.WriteLine($"Skipping test - PDF not found: {pdfPath}");
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

                output.WriteLine($"\n=== Font: {fontName} - {font?.BaseFont} ===");
                if (font is null) return;
                output.WriteLine($"Font Type: {font.FontType}");
                output.WriteLine($"FirstChar: {font.FirstChar}");
                output.WriteLine($"LastChar: {font.LastChar}");
                output.WriteLine($"Has ToUnicode: {font.ToUnicode != null}");
                output.WriteLine($"Has Encoding: {font.Encoding != null}");
                if (font.Encoding != null)
                {
                    output.WriteLine($"Encoding Type: {font.Encoding.GetType().Name}");
                }

                output.WriteLine($"UnitsPerEm: {metrics.UnitsPerEm}");
                output.WriteLine($"NumGlyphs: {metrics.NumGlyphs}");
                output.WriteLine($"Is CFF: {metrics.IsCffFont}");

                // Check if cmap table exists
                output.WriteLine($"\nDiagnostic info:");
                output.WriteLine($"Has cmap table: {metrics.HasCmapTable}");
                output.WriteLine($"Cmap subtable count: {metrics.GetCmapSubtableCount()}");
                output.WriteLine($"Cmap encoding record count: {metrics.GetCmapEncodingRecordCount()}");
                if (metrics.GetCmapEncodingRecordCount() > 0)
                {
                    for (var i = 0; i < metrics.GetCmapEncodingRecordCount(); i++)
                    {
                        output.WriteLine($"  Encoding {i}: {metrics.GetCmapEncodingRecordInfo(i)}");
                    }
                }

                // Test character->glyph mapping for problematic characters
                output.WriteLine("\nTesting character->glyph mapping:");
                int[] testChars = [32, 40, 41, 44, 45, 46, 47, 48];

                foreach (int charCode in testChars)
                {
                    ushort glyphId = metrics.GetGlyphId((ushort)charCode);
                    ushort advanceWidth = metrics.GetAdvanceWidth(glyphId);
                    double pdfWidth = font.GetCharacterWidth(charCode);

                    output.WriteLine($"  Char {charCode} ('{(char)charCode}'): " +
                                      $"GlyphID={glyphId}, " +
                                      $"AdvanceWidth={advanceWidth}, " +
                                      $"PDF Width={pdfWidth:F2}");
                }

                return; // Only test the first font
            }
        }
    }
}
