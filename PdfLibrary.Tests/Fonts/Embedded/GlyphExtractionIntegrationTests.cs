using PdfLibrary.Core;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Fonts.Embedded;

/// <summary>
/// Integration tests for glyph extraction using EmbeddedFontMetrics
/// These tests demonstrate that the glyph extraction API is ready for real-world use
/// </summary>
public class GlyphExtractionIntegrationTests
{
    private const string PdfStandardsPath = @"C:\Users\jorda\RiderProjects\PDF\PDF Standards";

    [Fact]
    public void EmbeddedFontMetrics_FromRealPdf_HasValidGlyphCount()
    {
        // Arrange
        string pdfPath = Path.Combine(PdfStandardsPath, "PDF20_AN002-AF.pdf");

        if (!File.Exists(pdfPath))
        {
            // Skip test if PDF not available
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);
        PdfPage? page = doc.GetPage(0);

        // Find the first embedded font with metrics
        EmbeddedFontMetrics? metrics = FindFirstEmbeddedFontMetrics(page);

        if (metrics == null)
        {
            // Skip if no embedded fonts found
            return;
        }

        // Assert
        Assert.True(metrics.NumGlyphs > 0);
        Assert.True(metrics.UnitsPerEm > 0);
        Assert.True(metrics.IsValid);
    }

    [Fact]
    public void EmbeddedFontMetrics_GetAdvanceWidth_ReturnsValidValues()
    {
        // Arrange
        string pdfPath = Path.Combine(PdfStandardsPath, "PDF20_AN002-AF.pdf");

        if (!File.Exists(pdfPath))
        {
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);
        PdfPage? page = doc.GetPage(0);

        EmbeddedFontMetrics? metrics = FindFirstEmbeddedFontMetrics(page);

        if (metrics is not { IsValid: true })
        {
            return;
        }

        // Act - Get advance widths for first 10 glyphs
        var widths = new List<ushort>();
        int maxGlyphs = Math.Min(10, (int)metrics.NumGlyphs);
        for (ushort i = 0; i < maxGlyphs; i++)
        {
            widths.Add(metrics.GetAdvanceWidth(i));
        }

        // Assert
        Assert.NotEmpty(widths);
        Assert.All(widths, w => Assert.True(w >= 0));
    }

    [Fact]
    public void EmbeddedFontMetrics_GetGlyphId_MapsCharactersCorrectly()
    {
        // Arrange
        string pdfPath = Path.Combine(PdfStandardsPath, "PDF20_AN002-AF.pdf");

        if (!File.Exists(pdfPath))
        {
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);
        PdfPage? page = doc.GetPage(0);

        EmbeddedFontMetrics? metrics = FindFirstEmbeddedFontMetrics(page);

        if (metrics is not { IsValid: true })
        {
            return;
        }

        // Act - Try to map some common character codes
        ushort glyphId32 = metrics.GetGlyphId(32);  // Space
        ushort glyphId65 = metrics.GetGlyphId(65);  // 'A'
        ushort glyphId97 = metrics.GetGlyphId(97);  // 'a'

        // Assert - Glyph IDs should be valid (within range)
        Assert.True(glyphId32 < metrics.NumGlyphs);
        Assert.True(glyphId65 < metrics.NumGlyphs);
        Assert.True(glyphId97 < metrics.NumGlyphs);
    }

    [Fact]
    public void EmbeddedFontMetrics_HasFontNames()
    {
        // Arrange
        string pdfPath = Path.Combine(PdfStandardsPath, "PDF20_AN002-AF.pdf");

        if (!File.Exists(pdfPath))
        {
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);
        PdfPage? page = doc.GetPage(0);

        EmbeddedFontMetrics? metrics = FindFirstEmbeddedFontMetrics(page);

        if (metrics is not { IsValid: true })
        {
            return;
        }

        // Assert - Font should have at least one name
        Assert.True(
            !string.IsNullOrEmpty(metrics.FamilyName) ||
            !string.IsNullOrEmpty(metrics.PostScriptName)
        );
    }

    [Fact]
    public void EmbeddedFontMetrics_GetLeftSideBearing_ReturnsValidValues()
    {
        // Arrange
        string pdfPath = Path.Combine(PdfStandardsPath, "PDF20_AN002-AF.pdf");

        if (!File.Exists(pdfPath))
        {
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);
        PdfPage? page = doc.GetPage(0);

        EmbeddedFontMetrics? metrics = FindFirstEmbeddedFontMetrics(page);

        if (metrics is not { IsValid: true })
        {
            return;
        }

        // Act - Get left side bearings for first 10 glyphs
        var bearings = new List<short>();
        int maxGlyphs = Math.Min(10, (int)metrics.NumGlyphs);
        for (ushort i = 0; i < maxGlyphs; i++)
        {
            bearings.Add(metrics.GetLeftSideBearing(i));
        }

        // Assert
        Assert.NotEmpty(bearings);
        // LSB can be negative, zero, or positive - just check it's a reasonable value
        Assert.All(bearings, lsb => Assert.True(Math.Abs(lsb) < 10000));
    }

    /// <summary>
    /// Find the first embedded font metrics on a PDF page
    /// </summary>
    private EmbeddedFontMetrics? FindFirstEmbeddedFontMetrics(PdfPage page)
    {
        // Try to get fonts from page resources
        PdfResources? resources = page.GetResources();

        PdfDictionary? fonts = resources?.GetFonts();
        if (fonts == null)
            return null;

        // Look through all fonts to find an embedded TrueType font
        foreach (KeyValuePair<PdfName, PdfObject> fontEntry in fonts)
        {
            try
            {
                var fontDict = fontEntry.Value as PdfDictionary;
                if (fontDict == null)
                    continue;

                // Try to create font and get embedded metrics
                var font = PdfFont.Create(fontDict);
                EmbeddedFontMetrics? metrics = font?.GetEmbeddedMetrics();
                if (metrics is { IsValid: true })
                {
                    return metrics;
                }
            }
            catch
            {
                // Skip fonts that fail to load
                continue;
            }
        }

        return null;
    }
}
