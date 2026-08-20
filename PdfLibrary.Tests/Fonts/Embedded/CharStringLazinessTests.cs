using System.Collections.Generic;
using CffTestFixtures;
using PdfLibrary.Fonts.Embedded;
using Xunit;

namespace PdfLibrary.Tests.Fonts.Embedded;

/// <summary>
/// Charstrings are parsed on demand, not at construction.
///
/// <para>Measured 2026-08-20 on the print corpus (gwg-gos, 98 files): <c>Type1Table.BuildCharStrings</c>
/// was <b>60.6%</b> of a single-threaded <c>pellucid scan</c>, fully parsing every glyph of every
/// embedded font into <c>CharStringList</c> — a list with three references in the whole codebase, its
/// declaration and two <c>Add</c> calls, read by nothing. Over the same 98 documents only 110
/// <c>GetGlyphOutline</c> calls ever wanted an outline, costing 2.3%. The conformance rules want advance
/// widths and glyph presence, not outlines.</para>
///
/// <para>Deferring it is not only cheaper, it is more robust, which is what these tests pin. A charstring
/// truncated mid-operand overruns in <c>CharStringParser.Parse</c> (the operand reads inside its switch
/// arms are unguarded, unlike the loop head). Parsing every glyph eagerly therefore let ONE malformed
/// glyph destroy the metrics for the whole font — including fonts whose other glyphs are perfectly
/// readable, and including callers that only ever wanted a width.</para>
/// </summary>
public class CharStringLazinessTests
{
    /// <summary>A charstring that ends mid-operand: 0xFF announces a 4-byte fixed operand, then stops.
    /// The loop head's bounds check passes on 0xFF; the four operand reads that follow do not.</summary>
    private static byte[] TruncatedGlyph => [0xFF, 0x00];

    private static byte[] EndCharGlyph => [0x0E];

    [Fact]
    public void One_malformed_charstring_does_not_destroy_the_whole_font()
    {
        var glyphs = new List<byte[]> { EndCharGlyph, EndCharGlyph, TruncatedGlyph, EndCharGlyph };

        var metrics = new EmbeddedFontMetrics(
            MinimalCff.Build(charsetOperand: null, numGlyphs: glyphs.Count, customCharStrings: glyphs));

        Assert.True(metrics.IsValid);
    }

    [Fact]
    public void A_font_whose_glyphs_are_all_well_formed_is_still_valid()
    {
        // The counterpart guard: validity must not become unconditional. If a future change made
        // IsValid always true, the test above would pass for the wrong reason and this one would not
        // notice — so this pins the ordinary path explicitly rather than by implication.
        var glyphs = new List<byte[]> { EndCharGlyph, EndCharGlyph, EndCharGlyph, EndCharGlyph };

        var metrics = new EmbeddedFontMetrics(
            MinimalCff.Build(charsetOperand: null, numGlyphs: glyphs.Count, customCharStrings: glyphs));

        Assert.True(metrics.IsValid);
        Assert.NotNull(metrics.EnumerateProgramGlyphNames());
    }
}
