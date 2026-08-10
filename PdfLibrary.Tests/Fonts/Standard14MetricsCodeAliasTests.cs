using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// L-4 final review finding 1: WinAnsiEncoding is incomplete for five codes that the AFMs do
/// carry — either it names a glyph the AFM doesn't recognise (0xA0 "nonbreakingspace" vs AFM
/// "space"), or it has no name at all (0xAD, 0x88, 0x98, 0xB5). WidthByCode's CodeAliases map
/// overrides those five with the AFM-recognised glyph name; the width itself still comes from
/// WidthByName per face, so these rows double as a per-face regression pin against the vendored
/// AFM data (see the class comment in Standard14MetricsAfmTests for why the AFM is authoritative).
/// </summary>
public class Standard14MetricsCodeAliasTests
{
    [Theory]
    [InlineData("Helvetica", 0xA0, 278)]        // no-break space -> space
    [InlineData("Helvetica-Bold", 0xA0, 278)]
    [InlineData("Times-Roman", 0xA0, 250)]
    [InlineData("Times-Bold", 0xA0, 250)]
    [InlineData("Helvetica", 0xAD, 333)]        // soft hyphen -> hyphen
    [InlineData("Helvetica-Bold", 0xAD, 333)]
    [InlineData("Times-Roman", 0xAD, 333)]
    [InlineData("Times-Bold", 0xAD, 333)]
    [InlineData("Helvetica", 0x88, 333)]        // circumflex
    [InlineData("Helvetica-Bold", 0x88, 333)]
    [InlineData("Times-Roman", 0x88, 333)]
    [InlineData("Times-Bold", 0x88, 333)]
    [InlineData("Helvetica", 0x98, 333)]        // tilde
    [InlineData("Helvetica-Bold", 0x98, 333)]
    [InlineData("Times-Roman", 0x98, 333)]
    [InlineData("Times-Bold", 0x98, 333)]
    [InlineData("Helvetica", 0xB5, 556)]        // micro -> mu; this one is a strict regression fix,
    [InlineData("Helvetica-Bold", 0xB5, 611)]   // the old fabricated table happened to answer 556 for
    [InlineData("Times-Roman", 0xB5, 500)]      // Helvetica by accident (right value, wrong reason)
    [InlineData("Times-Bold", 0xB5, 556)]
    public void FiveWinAnsiCodesResolveThroughTheAliasMap(string baseFont, int code, double expected)
    {
        Assert.Equal(expected, Standard14Metrics.WidthByCode(baseFont, code));
    }
}
