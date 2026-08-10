using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// WidthByCode is the no-/Encoding fallback. It stays WinAnsi — that is what the previous tables
/// were, and changing the assumed built-in encoding is a separate question — but it now derives
/// from the AFM data through WinAnsiEncoding rather than duplicating it in hand-written tables.
/// </summary>
public class Standard14MetricsByCodeTests
{
    [Theory]
    // WinAnsi 0x27 is quotesingle and 0x60 is grave — NOT quoteright/quoteleft, which is what the
    // AFM's own C codes say. This pins the WinAnsi reading explicitly so the two cannot be conflated
    // again the way they were before.
    [InlineData("Helvetica", 0x27, 191)]   // quotesingle
    [InlineData("Helvetica", 0x60, 333)]   // grave
    [InlineData("Times-Roman", 0x27, 180)]
    public void WinAnsiCodesResolveByTheWinAnsiReading(string baseFont, int code, double expected)
    {
        Assert.Equal(expected, Standard14Metrics.WidthByCode(baseFont, code));
    }

    [Theory]
    // Codes 127-255 fell off the end of the old tables and hit the catch-all.
    [InlineData("Helvetica", 0x91, 222)]   // quoteleft
    [InlineData("Helvetica", 0x92, 222)]   // quoteright
    [InlineData("Helvetica", 0x95, 350)]   // bullet
    [InlineData("Helvetica", 0x97, 1000)]  // emdash
    [InlineData("Helvetica", 0xE9, 556)]   // eacute
    public void TheExtendedRangeIsNowCovered(string baseFont, int code, double expected)
    {
        Assert.Equal(expected, Standard14Metrics.WidthByCode(baseFont, code));
    }

    [Fact]
    public void EachTimesFaceGetsItsOwnMetrics()
    {
        // The old by-code switch routed all four Times faces to GetTimesRomanWidth. 'a' is 0x61.
        Assert.Equal(444, Standard14Metrics.WidthByCode("Times-Roman", 0x61));
        Assert.Equal(500, Standard14Metrics.WidthByCode("Times-Bold", 0x61));
        Assert.Equal(500, Standard14Metrics.WidthByCode("Times-Italic", 0x61));
        Assert.Equal(500, Standard14Metrics.WidthByCode("Times-BoldItalic", 0x61));
    }

    [Fact]
    public void CourierStaysFlat()
    {
        Assert.Equal(600, Standard14Metrics.WidthByCode("Courier", 0x61));
        Assert.Equal(600, Standard14Metrics.WidthByCode("Courier-Bold", 0xE9));
    }

    [Fact]
    public void SymbolAndDingbatsReturnNullByCode()
    {
        // Their built-in encodings are not WinAnsi, so a WinAnsi code says nothing about which glyph
        // is meant. By NAME they resolve; by code they must not guess.
        Assert.Null(Standard14Metrics.WidthByCode("Symbol", 0x61));
        Assert.Null(Standard14Metrics.WidthByCode("ZapfDingbats", 0x61));
    }

    [Theory]
    [InlineData(0x00)]  // no WinAnsi glyph
    [InlineData(0x1F)]
    public void UnmappedCodesReturnNull(int code)
    {
        Assert.Null(Standard14Metrics.WidthByCode("Helvetica", code));
    }

    [Fact]
    public void AnUnknownBaseFontReturnsNull()
    {
        Assert.Null(Standard14Metrics.WidthByCode("FooCorpSans", 0x41));
    }
}
