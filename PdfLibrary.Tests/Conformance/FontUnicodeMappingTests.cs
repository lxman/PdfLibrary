using PdfLibrary.Conformance;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Pins the asymmetry between <c>FontUnicodeMapping</c>'s private, syntax-only
/// <c>IsUnicodeGlyphName</c> (what the conservative <see cref="FontUnicodeMapping.HasReliableUnicode"/>
/// consults) and its internal, value-producing <see cref="FontUnicodeMapping.UnicodeGlyphNameValue"/>
/// (what <c>FontRemediationPlanner</c> consults). A prior revision collapsed the two into one method,
/// which silently tightened the conformance rule: a name that only fits the uXXXXXX SYNTAX but does
/// not encode a valid Unicode scalar value (a surrogate, or a value above U+10FFFF) newly failed
/// <c>pdfa2u-tounicode</c> with no fixture ever exercising the difference. <c>IsUnicodeGlyphName</c>
/// itself is `private` — not reachable even via <c>InternalsVisibleTo</c> — so its behaviour is pinned
/// here indirectly through the public <see cref="FontUnicodeMapping.HasReliableUnicode"/>, exactly the
/// way the real rule consumes it.
/// </summary>
public class FontUnicodeMappingTests
{
    private static PdfName N(string s) => new(s);

    // The four inputs the round-2 review identified as divergent: syntactically valid uXXXXXX names
    // (5–7 chars, 'u' + hex digits) whose hex digits do NOT form a valid Unicode scalar value —
    // uD800/uDFFF are surrogate code points, u110000/uFFFFFF are above the U+10FFFF ceiling.
    [Theory]
    [InlineData("uD800")]
    [InlineData("uDFFF")]
    [InlineData("u110000")]
    [InlineData("uFFFFFF")]
    public void HasReliableUnicode_GivesBenefitOfDoubtToASyntacticallyValidButOutOfRangeName(string glyphName)
    {
        // Syntax alone is positive-evidence-of-no-mapping's absence: the RULE is conservative by
        // design and must not fault a name merely because the code point it would encode is invalid.
        Assert.True(FontUnicodeMapping.HasReliableUnicode(
            Ctx(), FontFor(glyphName), 0x41));
    }

    [Theory]
    [InlineData("uD800")]
    [InlineData("uDFFF")]
    [InlineData("u110000")]
    [InlineData("uFFFFFF")]
    public void UnicodeGlyphNameValue_RefusesToDeriveAnOutOfRangeCodePoint(string glyphName)
    {
        // The PLANNER cannot stand behind a value that does not exist — this is the opposite answer
        // from HasReliableUnicode for the exact same input, and that asymmetry is the point.
        Assert.Null(FontUnicodeMapping.UnicodeGlyphNameValue(glyphName));
    }

    // The non-divergent case, pinned so a future edit collapsing the two methods again shows up as a
    // near-miss rather than only as new Assert.True/Assert.Null failures above: an IN-RANGE uXXXXXX
    // name must agree on both sides — reliable per the rule, AND a real derived value for the planner.
    [Fact]
    public void InRangeUConventionName_AgreesOnBothSides()
    {
        Assert.True(FontUnicodeMapping.HasReliableUnicode(Ctx(), FontFor("u0041"), 0x41));
        Assert.Equal("A", FontUnicodeMapping.UnicodeGlyphNameValue("u0041"));
    }

    /// <summary>
    /// A simple TrueType font with no /Encoding and no /ToUnicode — the shape of the corpus fixture
    /// "veraPDF test suite 6-2-11-7-2-t01-fail-e.pdf" (symbolic subset Cambria, codes 1-4). There is no
    /// PDF-level mechanism left to answer with: the embedded program's cmap maps codes to GLYPHS, and a
    /// symbolic (3,0) table maps into the private use area, so it is not a Unicode answer either. This
    /// is positive evidence of no mapping, not an unknown, and veraPDF fails the fixture on exactly it.
    /// </summary>
    [Fact]
    public void HasReliableUnicode_IsFalseForASimpleTrueTypeCodeWithNoGlyphName()
    {
        Assert.False(FontUnicodeMapping.HasReliableUnicode(Ctx(), UnencodedFont("TrueType"), 0x01));
    }

    /// <summary>
    /// The Type1/CFF arm deliberately keeps the benefit of the doubt. The engine DOES read a Type1/CFF
    /// program's built-in encoding, so arguably a null name there is positive evidence too — but no
    /// corpus fixture exercises it, and tightening it would be an unmeasured precision trade in a rule
    /// whose whole point is to avoid false positives. Pinned so the TrueType change cannot quietly
    /// generalise.
    /// </summary>
    [Fact]
    public void HasReliableUnicode_StillGivesBenefitOfDoubtToASimpleType1CodeWithNoGlyphName()
    {
        Assert.True(FontUnicodeMapping.HasReliableUnicode(Ctx(), UnencodedFont("Type1"), 0x01));
    }

    /// <summary>The new TrueType arm must fire ONLY on the no-name path: a TrueType code the encoding
    /// does name, and names with an AGL glyph, is mapped and must stay unflagged. This is the
    /// over-firing guard — without it the arm could degenerate into "all TrueType fails".</summary>
    [Fact]
    public void HasReliableUnicode_IsTrueForATrueTypeCodeWhoseEncodingNamesAnAglGlyph()
    {
        Assert.True(FontUnicodeMapping.HasReliableUnicode(Ctx(), TrueTypeFontFor("A"), 0x41));
    }

    private static ConformanceContext Ctx() => new(new PdfDocument(), ConformanceProfile.PdfA2u);

    /// <summary>A simple font of <paramref name="subtype"/> with NO /Encoding entry at all, so the
    /// encoding can produce no glyph name for a low code.</summary>
    private static PdfFont UnencodedFont(string subtype)
    {
        var dict = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N(subtype),
            [N("BaseFont")] = N("BAAAAA+Cambria"),
            [N("FirstChar")] = new PdfInteger(0),
            [N("LastChar")] = new PdfInteger(4),
        };
        return PdfFont.Create(dict)!;
    }

    /// <summary>A TrueType font whose /Differences names code 0x41 <paramref name="glyphName"/>.</summary>
    private static PdfFont TrueTypeFontFor(string glyphName)
    {
        var dict = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N("CustomTrueType"),
            [N("Encoding")] = new PdfDictionary
            {
                [N("BaseEncoding")] = N("WinAnsiEncoding"),
                [N("Differences")] = new PdfArray(new PdfInteger(0x41), N(glyphName)),
            },
        };
        return PdfFont.Create(dict)!;
    }

    /// <summary>A direct (non-indirect, unregistered) Type1 font dictionary whose /Differences maps
    /// code 0x41 to <paramref name="glyphName"/>. Direct construction is sufficient here —
    /// <c>HasReliableUnicode</c>'s simple-font path never resolves an indirect reference.</summary>
    private static PdfFont FontFor(string glyphName)
    {
        var dict = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type1"),
            [N("BaseFont")] = N("CustomFont"),
            [N("Encoding")] = new PdfDictionary
            {
                [N("BaseEncoding")] = N("WinAnsiEncoding"),
                [N("Differences")] = new PdfArray(new PdfInteger(0x41), N(glyphName)),
            },
        };
        return Assert.IsType<Type1Font>(PdfFont.Create(dict));
    }
}
