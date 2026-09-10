using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Fonts.Embedded;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// F-4a Task 1: the shared width enumeration extracted from FontProgramRule so rule and repair
/// cannot disagree about which GID a code's width comparison used (the F-3 SubsetProgramGlyphs
/// precedent). Fixtures reuse the promoted <see cref="ZeroAdvanceSfntFixture"/> byte builders
/// (shared with FontProgramZeroAdvanceTests) and the same document shape as its ZeroAdvanceDoc.
/// </summary>
public class ProgramWidthResolverTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    /// <summary>Same document shape as FontProgramZeroAdvanceTests.ZeroAdvanceDoc: a TrueType
    /// font, /FirstChar 10 /Widths [507], code 10 shown once in a Tj hex string, with gid 1's
    /// hmtx advance parameterized so the same shape covers both the zero-advance skip and an
    /// ordinary measurable mismatch.</summary>
    private static PdfDocument Doc(ushort gid1Advance, bool symbolic = false)
    {
        byte[] font = symbolic
            ? ZeroAdvanceSfntFixture.SymbolFontBytes(gid1Advance)
            : ZeroAdvanceSfntFixture.FontBytes(gid1Advance);
        var doc = new PdfDocument();
        doc.AddObject(3, 0, new PdfStream(
            new PdfDictionary { [N("Length1")] = new PdfInteger(font.Length) }, font));
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEE+ZeroAdvance"),
            [N("Flags")] = new PdfInteger(symbolic ? 4 : 32),
            [N("FontFile2")] = Ref(3),
        });
        doc.AddObject(1, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N("ABCDEE+ZeroAdvance"),
            [N("FirstChar")] = new PdfInteger(10),
            [N("LastChar")] = new PdfInteger(10),
            [N("Widths")] = new PdfArray(new PdfInteger(507)),
            [N("FontDescriptor")] = Ref(2),
        });
        // Show code 10 in a Tj hex string so the used-glyph walk reaches the font.
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes("BT /F0 12 Tf <0A> Tj ET")));
        doc.AddObject(22, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(21),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary
            {
                [N("Font")] = new PdfDictionary { [N("F0")] = Ref(1) },
            },
        });
        doc.AddObject(21, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(Ref(22)),
            [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(20, 0, new PdfDictionary
        {
            [N("Type")] = N("Catalog"),
            [N("Pages")] = Ref(21),
        });
        doc.Trailer.Dictionary[N("Root")] = Ref(20);
        return doc;
    }

    /// <summary>Resolves the font, its embedded metrics, and its /Widths array from a Doc fixture
    /// via the same used-text-glyph walk FontProgramRule.Check uses, so the fixture is proven
    /// reachable the way the rule reaches it rather than via a shortcut PdfFont.Create call.</summary>
    private static (PdfFont Font, EmbeddedFontMetrics Metrics, PdfArray Widths) Load(ushort gid1Advance)
    {
        var context = new ConformanceContext(Doc(gid1Advance), ConformanceProfile.PdfA2b);
        UsedFontCodes usage = context.UsedTextGlyphs.Single();
        EmbeddedFontMetrics metrics = usage.Font.GetEmbeddedMetrics()!;
        var widths = (PdfArray)context.Resolve(usage.Font.FontDictionary.Get("Widths"))!;
        return (usage.Font, metrics, widths);
    }

    [Fact]
    public void GetGlyphIdByUnicode_resolves_through_the_cmap_and_rejects_out_of_bmp()
    {
        var metrics = new EmbeddedFontMetrics(ZeroAdvanceSfntFixture.FontBytes()); // cmap maps code 10 -> gid 1
        Assert.Equal(1, metrics.GetGlyphIdByUnicode(10));
        Assert.Equal(0, metrics.GetGlyphIdByUnicode(0));
        Assert.Equal(0, metrics.GetGlyphIdByUnicode(0x10000)); // out of BMP: never truncate
    }

    [Fact]
    public void Zero_advance_codes_are_skipped_not_yielded()
    {
        // The issue-26 semantics: a real gid whose advance is 0 is unmeasurable, same as gid 0.
        // The fixture maps code 10 -> gid 1 with advance 0; the resolver must yield nothing for it
        // (fixture font dict: /FirstChar 10, /Widths [507]).
        (PdfFont font, EmbeddedFontMetrics metrics, PdfArray widths) = Load(gid1Advance: 0);
        Assert.Empty(ProgramWidthResolver.Simple(font, metrics, widths, [10], isTrueType: true));
    }

    [Fact]
    public void A_measurable_mismatch_yields_the_gid_the_advance_came_from()
    {
        // Same fixture but hmtx gid 1 advance = 450; /Widths [507].
        (PdfFont font, EmbeddedFontMetrics metrics, PdfArray widths) = Load(gid1Advance: 450);
        WidthComparison w = Assert.Single(
            ProgramWidthResolver.Simple(font, metrics, widths, [10], isTrueType: true));
        Assert.Equal(10, w.Code);
        Assert.Equal(1, w.Gid);
        Assert.Equal(507, w.Declared);
        Assert.Equal(450, w.Program); // upm 1000 → no scaling distortion
    }

    [Fact]
    public void A_used_code_below_first_char_uses_the_default_missing_width()
    {
        // Issue 29's minimal shape: the content shows code 10, but /FirstChar starts at 20 so the
        // /Widths array has no entry for it. The descriptor has no /MissingWidth, whose specified
        // default is 0; the cmap still maps code 10 to GID 1 with a real 450-unit advance.
        PdfDocument doc = Doc(gid1Advance: 450);
        ((PdfDictionary)doc.Objects[1])[N("FirstChar")] = new PdfInteger(20);

        var context = new ConformanceContext(doc, ConformanceProfile.PdfA2b);
        UsedFontCodes usage = context.UsedTextGlyphs.Single();
        EmbeddedFontMetrics metrics = usage.Font.GetEmbeddedMetrics()!;
        var widths = (PdfArray)context.Resolve(usage.Font.FontDictionary.Get("Widths"))!;

        WidthComparison comparison = Assert.Single(
            ProgramWidthResolver.Simple(usage.Font, metrics, widths, [10], isTrueType: true));
        Assert.Equal(10, comparison.Code);
        Assert.Equal(1, comparison.Gid);
        Assert.Equal(0, comparison.Declared);
        Assert.Equal(450, comparison.Program);
    }

    [Fact]
    public void An_out_of_range_code_uses_an_explicit_descriptor_missing_width()
    {
        PdfDocument doc = Doc(gid1Advance: 450);
        ((PdfDictionary)doc.Objects[1])[N("FirstChar")] = new PdfInteger(20);
        ((PdfDictionary)doc.Objects[2])[N("MissingWidth")] = new PdfInteger(333);

        var context = new ConformanceContext(doc, ConformanceProfile.PdfA2b);
        UsedFontCodes usage = context.UsedTextGlyphs.Single();
        EmbeddedFontMetrics metrics = usage.Font.GetEmbeddedMetrics()!;
        var widths = (PdfArray)context.Resolve(usage.Font.FontDictionary.Get("Widths"))!;

        WidthComparison comparison = Assert.Single(
            ProgramWidthResolver.Simple(usage.Font, metrics, widths, [10], isTrueType: true));
        Assert.Equal(333, comparison.Declared);
        Assert.Equal(450, comparison.Program);
    }

    [Fact]
    public void A_symbolic_truetype_width_uses_its_windows_symbol_cmap()
    {
        // Issue 30: Windows-Symbol cmaps conventionally key an 8-bit PDF code in U+F000..U+F0FF.
        // Name/Unicode and raw-code lookup both miss code 10 here; U+F00A is the authoritative gid.
        PdfDocument doc = Doc(gid1Advance: 450, symbolic: true);
        var context = new ConformanceContext(doc, ConformanceProfile.PdfA2b);
        UsedFontCodes usage = context.UsedTextGlyphs.Single();
        EmbeddedFontMetrics metrics = usage.Font.GetEmbeddedMetrics()!;
        var widths = (PdfArray)context.Resolve(usage.Font.FontDictionary.Get("Widths"))!;

        Assert.True(metrics.HasSymbolCmapEncoding());
        Assert.Equal(0, metrics.GetGlyphId(10));
        WidthComparison comparison = Assert.Single(
            ProgramWidthResolver.Simple(usage.Font, metrics, widths, [10], isTrueType: true));
        Assert.Equal(1, comparison.Gid);
        Assert.Equal(507, comparison.Declared);
        Assert.Equal(450, comparison.Program);
    }
}
