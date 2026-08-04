using CffTestFixtures;
using FontParser.Tables.Cff.Type1;
using FontParser.Tables.Cff.Type1.Charsets;
using Xunit;

namespace FontParser.Tests;

/// <summary>
/// Top DICT charset handling for the PREDEFINED charsets (Adobe TN #5176 Table 9 and §14). The operator
/// is optional and defaults to 0 (ISOAdobe), and a predefined value 0/1/2 names a built-in charset rather
/// than an offset — there is no table in the font data to read. Both cases used to be mishandled: an
/// absent operator threw out of the constructor, and an explicit <c>charset 0</c> seeked to byte 0 and
/// parsed the CFF header as a charset table.
/// </summary>
public class Type1TablePredefinedCharsetTests
{
    [Fact]
    public void AbsentCharsetOperator_ParsesAsIsoAdobe()
    {
        var t = new Type1Table(MinimalCff.Build(charsetOperand: null, numGlyphs: 4)); // must NOT throw

        var f0 = Assert.IsType<CharsetsFormat0>(t.CharSet);
        Assert.Equal(new ushort[] { 1, 2, 3 }, f0.Glyphs); // GID i -> SID i, .notdef not encoded
        Assert.Equal(4, t.RawCharStrings.Count);
    }

    [Fact]
    public void ExplicitCharsetZero_MatchesAbsentOperator_AndDoesNotParseTheHeader()
    {
        var absent = new Type1Table(MinimalCff.Build(charsetOperand: null, numGlyphs: 4));
        var explicitZero = new Type1Table(MinimalCff.Build(charsetOperand: 0, numGlyphs: 4));

        var a = Assert.IsType<CharsetsFormat0>(absent.CharSet);
        var e = Assert.IsType<CharsetsFormat0>(explicitZero.CharSet);
        Assert.Equal(a.Glyphs, e.Glyphs);

        // The CFF header is 01 00 04 01, so parsing it as a format-0 charset would yield SID 0x0004
        // for GID 1 (and then read on into the Name INDEX). The identity mapping proves it did not.
        Assert.Equal(new ushort[] { 1, 2, 3 }, e.Glyphs);
    }

    [Fact]
    public void IsoAdobe_StopsAtSid228_ForAFontWithMoreGlyphs()
    {
        var t = new Type1Table(MinimalCff.Build(charsetOperand: 0, numGlyphs: 300));

        var f0 = Assert.IsType<CharsetsFormat0>(t.CharSet);
        Assert.Equal(228, f0.Glyphs.Count); // ISOAdobe defines SIDs 1..228 and no more
        Assert.Equal(228, f0.Glyphs[^1]);
    }

    /// <summary>
    /// The Expert arm is the one that discriminates against the old code: <c>charset 1</c> used to seek to
    /// byte 1 and read the header's minor-version byte (0x00) as a format-0 charset marker, producing
    /// <c>Glyphs = [1025, 1, 257]</c> — header and Name INDEX bytes presented as glyph SIDs.
    /// </summary>
    [Fact]
    public void PredefinedExpertCharset_IsNotParsedFromTheHeader()
    {
        var t = new Type1Table(MinimalCff.Build(charsetOperand: 1, numGlyphs: 4));

        Assert.Null(t.CharSet);
        Assert.Equal(4, t.RawCharStrings.Count);
    }

    /// <summary>
    /// ExpertSubset gets the same treatment, but be honest about what this test proves: it does NOT
    /// discriminate against the old code for this fixture. <c>charset 2</c> seeked to byte 2 and read
    /// hdrSize (0x04), which fell into the unrecognised-format arm and left the charset null for entirely
    /// the wrong reason. Making it bite would need a header lying about its own size — a worse fixture
    /// than an honest test with a narrow claim. Kept as a forward regression guard: it pins ExpertSubset
    /// to "null, and the rest of the font still parses".
    /// </summary>
    [Fact]
    public void PredefinedExpertSubsetCharset_LeavesCharsetNull()
    {
        var t = new Type1Table(MinimalCff.Build(charsetOperand: 2, numGlyphs: 4));

        Assert.Null(t.CharSet);
        Assert.Equal(4, t.RawCharStrings.Count);
    }

    [Fact]
    public void CustomCharset_StillParsedFromTheTable()
    {
        // Regression guard for the offset > 2 path: a real format-0 table must still win over the default.
        var t = new Type1Table(MinimalCff.Build(charsetOperand: null, numGlyphs: 4,
            customCharsetSids: [40, 41, 42]));

        var f0 = Assert.IsType<CharsetsFormat0>(t.CharSet);
        Assert.Equal(new ushort[] { 40, 41, 42 }, f0.Glyphs);
        Assert.NotEmpty(t.RawCharset);
    }

    /// <summary>
    /// A CID-keyed CFF with no charset must NOT get the ISOAdobe list. A CID charset holds CIDs, and the
    /// 228 bound is a SID bound — synthesizing it would make every CID ≥ 229 in a large font resolve to
    /// .notdef while the font still reported itself valid, i.e. blank text that looks fine. Null instead,
    /// so callers decline. The font must still parse: that is the part that beats the old throw.
    /// </summary>
    [Fact]
    public void CidKeyedCffWithNoCharset_DoesNotSynthesizeIsoAdobe()
    {
        var t = new Type1Table(MinimalCff.BuildCid(numGlyphs: 300)); // must NOT throw

        Assert.True(t.IsCid);
        Assert.Null(t.CharSet);
        Assert.Equal(300, t.RawCharStrings.Count);
        Assert.Equal(-1, t.GetGlyphIndexByCid(250)); // no charset -> no answer, rather than a wrong one
    }
}
