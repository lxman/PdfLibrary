using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Structure;
using Xunit.Abstractions;

namespace PdfLibrary.Tests.Fonts.Embedded;

public class CmapDiagnosticTest
{
    private readonly ITestOutputHelper _output;

    public CmapDiagnosticTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void DiagnoseCmapParsing()
    {
        const string pdfPath = @"C:\Users\jorda\RiderProjects\PDF\PDF Standards\PDF20_AN002-AF.pdf";

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

                _output.WriteLine($"\n=== Font: {fontName} - {font?.BaseFont} ===");
                if (font is null) return;
                _output.WriteLine($"Font Type: {font.FontType}");
                _output.WriteLine($"FirstChar: {font.FirstChar}");
                _output.WriteLine($"LastChar: {font.LastChar}");
                _output.WriteLine($"Has ToUnicode: {font.ToUnicode != null}");
                _output.WriteLine($"Has Encoding: {font.Encoding != null}");
                if (font.Encoding != null)
                {
                    _output.WriteLine($"Encoding Type: {font.Encoding.GetType().Name}");
                }

                _output.WriteLine($"UnitsPerEm: {metrics.UnitsPerEm}");
                _output.WriteLine($"NumGlyphs: {metrics.NumGlyphs}");
                _output.WriteLine($"Is CFF: {metrics.IsCffFont}");

                // Check if cmap table exists
                _output.WriteLine($"\nDiagnostic info:");
                _output.WriteLine($"Has cmap table: {metrics.HasCmapTable}");
                _output.WriteLine($"Cmap subtable count: {metrics.GetCmapSubtableCount()}");
                _output.WriteLine($"Cmap encoding record count: {metrics.GetCmapEncodingRecordCount()}");
                if (metrics.GetCmapEncodingRecordCount() > 0)
                {
                    for (var i = 0; i < metrics.GetCmapEncodingRecordCount(); i++)
                    {
                        _output.WriteLine($"  Encoding {i}: {metrics.GetCmapEncodingRecordInfo(i)}");
                    }
                }

                // Test character->glyph mapping for problematic characters
                _output.WriteLine("\nTesting character->glyph mapping:");
                int[] testChars = [32, 40, 41, 44, 45, 46, 47, 48];

                foreach (int charCode in testChars)
                {
                    ushort glyphId = metrics.GetGlyphId((ushort)charCode);
                    ushort advanceWidth = metrics.GetAdvanceWidth(glyphId);
                    double pdfWidth = font.GetCharacterWidth(charCode);

                    _output.WriteLine($"  Char {charCode} ('{(char)charCode}'): " +
                                      $"GlyphID={glyphId}, " +
                                      $"AdvanceWidth={advanceWidth}, " +
                                      $"PDF Width={pdfWidth:F2}");
                }

                return; // Only test the first font
            }
        }
    }
}
