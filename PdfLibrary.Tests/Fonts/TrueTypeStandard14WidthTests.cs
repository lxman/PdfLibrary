using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// A non-embedded simple TrueType font may legally omit /Widths (and /FontDescriptor): the viewer is
/// required to supply advance widths from the Standard-14 AFM metrics of the recognized base font
/// (ISO 32000-1 §9.6.2.1). Acrobat PDFWriter 3.02 emits exactly such dicts — /BaseFont /TimesNewRoman,
/// /Arial, /CourierNew with /WinAnsiEncoding and nothing else. Before this fix TrueTypeFont fell back
/// to a hardcoded 500-unit advance for every glyph, producing gap-after-narrow / overlap-on-wide text.
/// </summary>
public class TrueTypeStandard14WidthTests
{
    private static PdfFont BareTrueType(string baseFont)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName("TrueType"),
            [new PdfName("BaseFont")] = new PdfName(baseFont),
            [new PdfName("Encoding")] = new PdfName("WinAnsiEncoding")
            // deliberately no /Widths, /FirstChar, /LastChar, /FontDescriptor
        };
        return PdfFont.Create(dict)!;
    }

    // 'i'=105, 'm'=109, 'W'=87, space=32. Expected values are the Adobe AFM advances for the
    // Standard-14 font the base name maps to (Times New Roman->Times-Roman, Arial->Helvetica,
    // Arial,Bold->Helvetica-Bold, Courier New->Courier). Without the fix every one returns 500.
    [Theory]
    // TimesNewRoman -> Times-Roman
    [InlineData("TimesNewRoman", 105, 278)]
    [InlineData("TimesNewRoman", 109, 778)]
    [InlineData("TimesNewRoman", 87, 944)]
    [InlineData("TimesNewRoman", 32, 250)]
    // Arial -> Helvetica
    [InlineData("Arial", 105, 222)]
    [InlineData("Arial", 109, 833)]
    // Arial,Bold -> Helvetica-Bold
    [InlineData("Arial,Bold", 109, 889)]
    // CourierNew -> Courier (monospace 600)
    [InlineData("CourierNew", 105, 600)]
    [InlineData("CourierNew", 109, 600)]
    public void NonEmbedded_NoWidths_UsesStandard14AfmMetrics(string baseFont, int charCode, double expected)
    {
        PdfFont font = BareTrueType(baseFont);
        Assert.Equal(expected, font.GetCharacterWidth(charCode));
    }

    // A present /Widths array is authoritative and must win over the AFM fallback, even for a
    // recognised Standard-14 base font. Guards the fallback's position (after the /Widths check).
    [Fact]
    public void ExplicitWidths_TakePrecedenceOverAfmMetrics()
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName("TrueType"),
            [new PdfName("BaseFont")] = new PdfName("TimesNewRoman"),
            [new PdfName("Encoding")] = new PdfName("WinAnsiEncoding"),
            [new PdfName("FirstChar")] = new PdfInteger(105),   // 'i'
            [new PdfName("LastChar")] = new PdfInteger(105),
            [new PdfName("Widths")] = new PdfArray { new PdfInteger(999) }
        };
        PdfFont font = PdfFont.Create(dict)!;

        Assert.Equal(999, font.GetCharacterWidth(105));        // the array, not the Times-Roman AFM 278
    }

    // A non-Standard-14 base font with no /Widths and no descriptor is untouched by this change: the
    // AFM lookups return null for it, so it still uses the historical 500-unit default.
    [Fact]
    public void UnknownFont_NoWidths_StillFallsBackToDefault()
    {
        PdfFont font = BareTrueType("SomeProprietaryFont");
        Assert.Equal(500, font.GetCharacterWidth(105));
    }
}
