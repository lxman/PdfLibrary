using CffTestFixtures;
using PdfLibrary.Fonts.Embedded;
using Xunit;

namespace PdfLibrary.Tests.Fonts.Embedded;

/// <summary>
/// What <see cref="EmbeddedFontMetrics"/> reports when a CFF program's charset cannot be determined.
/// Both enumerators must return <b>null</b> ("cannot answer"), never an empty-but-non-null set. A
/// non-null <c>{".notdef"}</c> is indistinguishable from a genuine one-glyph program, and
/// <c>FontSubsetCoverageRule</c> reads non-null as enumerable: its size guard then rejects every
/// declared /CharSet it is compared against, emitting a false-positive conformance error on a font whose
/// only sin is a predefined Expert charset.
/// </summary>
public class CffCharsetEnumerationTests
{
    [Fact]
    public void PredefinedExpertCharset_GlyphNameEnumerationDeclinesToAnswer()
    {
        var metrics = new EmbeddedFontMetrics(MinimalCff.Build(charsetOperand: 1, numGlyphs: 4));

        Assert.True(metrics.IsValid); // the program parses; only its charset is unknown
        Assert.Null(metrics.EnumerateProgramGlyphNames());
    }

    [Fact]
    public void IsoAdobeCharset_GlyphNameEnumerationStillAnswers()
    {
        // The counterpart guard: declining must be scoped to the unknown-charset case, not blanket.
        var metrics = new EmbeddedFontMetrics(MinimalCff.Build(charsetOperand: null, numGlyphs: 4));

        Assert.True(metrics.IsValid);
        IReadOnlySet<string>? names = metrics.EnumerateProgramGlyphNames();
        Assert.NotNull(names);
        Assert.Equal(4, names!.Count); // .notdef + SIDs 1..3 (space, exclam, quotedbl)
        Assert.Contains(".notdef", names);
        Assert.Contains("space", names);
    }

    [Fact]
    public void CidKeyedCffWithNoCharset_CidEnumerationDeclinesToAnswer()
    {
        var metrics = new EmbeddedFontMetrics(MinimalCff.BuildCid(numGlyphs: 300));

        Assert.True(metrics.IsValid);
        Assert.Null(metrics.EnumerateProgramCids());
    }
}
