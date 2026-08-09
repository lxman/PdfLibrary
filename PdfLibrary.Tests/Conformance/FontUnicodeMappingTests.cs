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

    private static ConformanceContext Ctx() => new(new PdfDocument(), ConformanceProfile.PdfA2u);

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
