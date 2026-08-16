using System.IO;
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Conformance;
using PdfLibrary.Tests.Fonts.Embedded;
using Xunit;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// Task 8 (spec Amendment 2026-08-15): ISO 32000-1 §9.6.6.2 — a SYMBOLIC simple Type1/CFF font whose
/// <c>/Encoding</c> is absent, or is a dict with no <c>/BaseEncoding</c>, gets its font program's OWN
/// built-in encoding as base, not StandardEncoding. Filed after Task 6's whole-branch review PROVED
/// issue 28's StandardEncoding upper-band fill (issue 28) minted a NEW width false positive and
/// WIDENED a rendering wrongness on exactly this shape — reproducer CC-MAIN <c>2000_2000078.pdf</c>
/// (symbolic Type1C Cyrillic Times clones, format-1 built-in CFF encoding mapping the upper band to
/// <c>afiiNNNNN</c> glyphs, <c>/Differences</c>-only <c>/Encoding</c> dict). Pre-fix, StandardEncoding
/// named code 208 "emdash" and the name-first resolution chain (width rule, renderer, extraction) all
/// resolved the WRONG Latin glyph instead of the font's own Cyrillic one.
/// </summary>
public class SymbolicBuiltInEncodingTests
{
    // ── Step 1: EmbeddedFontMetrics.GetCffGlyphNameByCharCode, against existing/new CFF fixtures ────

    [Fact]
    public void GetCffGlyphNameByCharCode_NoBuiltInEncoding_ReturnsNull()
    {
        // MinimalType1CFont (WidthPrecedenceTests.cs:351-469) writes format byte 0xFF — "no Encoding
        // parsed" (matches CffTestFixtures.MinimalCff's convention) — so there is no built-in Encoding
        // for any code to resolve through.
        var metrics = new EmbeddedFontMetrics(MinimalType1CFont.Build(600, 300));
        Assert.True(metrics.IsValid);
        Assert.True(metrics.IsCffFont);
        Assert.Equal((ushort)0, metrics.GetGlyphIdByCffEncoding((ushort)'A'));
        Assert.Null(metrics.GetCffGlyphNameByCharCode('A'));
    }

    [Fact]
    public void GetCffGlyphNameByCharCode_ResolvesThroughBuiltInEncodingAndCharset()
    {
        byte[] cff = SymbolicCffFixtureFont.Build(
            code: 208, customGlyphName: "afii10034", customAdvance: 576, emdashAdvance: 1000);
        var metrics = new EmbeddedFontMetrics(cff);

        Assert.True(metrics.IsValid);
        Assert.True(metrics.IsCffFont);
        Assert.Equal("afii10034", metrics.GetCffGlyphNameByCharCode(208));
        // A code the built-in Encoding does not map returns null, not a guess.
        Assert.Null(metrics.GetCffGlyphNameByCharCode(65));
    }

    [Fact]
    public void GetCffGlyphNameByCharCode_OutOfRangeOrNonCff_ReturnsNull()
    {
        var cffMetrics = new EmbeddedFontMetrics(MinimalType1CFont.Build(600, 300));
        Assert.Null(cffMetrics.GetCffGlyphNameByCharCode(-1));
        Assert.Null(cffMetrics.GetCffGlyphNameByCharCode(256));

        // The `!_isCffFont` guard itself: a real (non-CFF) TrueType program, same fixture
        // PreflightSlice19Tests uses. In range, in-code, but the wrong font kind entirely.
        byte[] trueTypeBytes = File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));
        var trueTypeMetrics = new EmbeddedFontMetrics(trueTypeBytes);
        Assert.True(trueTypeMetrics.IsValid);
        Assert.False(trueTypeMetrics.IsCffFont);
        Assert.Null(trueTypeMetrics.GetCffGlyphNameByCharCode('A'));
    }

    // ── Step 2: fixture-honesty preconditions ─────────────────────────────────────────────────────
    // Mirrors the real reproducer's shape (CC-MAIN 2000_2000078.pdf): code 208's built-in-encoding
    // name is NOT what StandardEncoding's Annex D.2 names that code ("emdash"), and "emdash" is a
    // REAL glyph elsewhere in the same charset — so the pre-fix name-first defect had something to
    // wrongly resolve to, not a dangling name.

    [Fact]
    public void Fixture_reproduces_the_false_positive_shape()
    {
        byte[] cff = SymbolicCffFixtureFont.Build(
            code: 208, customGlyphName: "afii10034", customAdvance: 576, emdashAdvance: 1000);
        var metrics = new EmbeddedFontMetrics(cff);

        Assert.NotEqual((ushort)0, metrics.GetGlyphIdByCffEncoding(208));
        Assert.Equal("afii10034", metrics.GetCffGlyphNameByCharCode(208));
        Assert.NotEqual((ushort)0, metrics.GetGlyphIdByName("emdash"));

        ushort customGid = metrics.GetGlyphIdByCffEncoding(208);
        ushort emdashGid = metrics.GetGlyphIdByName("emdash");
        ushort customAdvance = metrics.GetAdvanceWidth(customGid);
        ushort emdashAdvance = metrics.GetAdvanceWidth(emdashGid);
        Assert.True(System.Math.Abs(customAdvance - emdashAdvance) >= 100,
            $"advances too close to distinguish the bug: custom={customAdvance}, emdash={emdashAdvance}");
    }

    // ── Step 3: the behavioural tests (document level) ────────────────────────────────────────────

    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfArray Rect(int x0, int y0, int x1, int y1) =>
        new(new PdfInteger(x0), new PdfInteger(y0), new PdfInteger(x1), new PdfInteger(y1));

    private const byte Code = 208;
    private const string CustomGlyphName = "afii10034";
    private const int CustomAdvance = 576;
    private const int EmdashAdvance = 1000;

    private static byte[] CffBytes =>
        SymbolicCffFixtureFont.Build(Code, CustomGlyphName, CustomAdvance, EmdashAdvance);

    /// <summary>Differences-only encoding dict — the shape the real reproducer's obj 72 carries
    /// (Differences-only, no /BaseEncoding).</summary>
    private static PdfDictionary DifferencesOnlyEncoding() => new()
    {
        [N("Differences")] = new PdfArray(new PdfInteger(127), new PdfName("sterling")),
    };

    /// <summary>
    /// One-page document (object 10 = font) embedding <see cref="SymbolicCffFixtureFont"/> (or
    /// <paramref name="cffBytesOverride"/>, when the test needs a different embedded program —
    /// e.g. one with no built-in Encoding at all), showing code 208 (<c>&lt;D0&gt;</c>).
    /// <paramref name="flags"/> controls the descriptor's symbolic bit (6 = Serif|Symbolic, matching
    /// 52/56 descriptors in the CC-MAIN reproducer; 34 = Serif|Nonsymbolic pins Tasks 2-3's unmoved
    /// behaviour). <paramref name="encoding"/> is the font dict's /Encoding value (a dict, a NAME, or
    /// null for "absent entirely").
    /// </summary>
    private static PdfDocument BuildDoc(int flags, PdfObject? encoding, byte[]? cffBytesOverride = null)
    {
        var doc = new PdfDocument();
        doc.AddObject(12, 0, new PdfStream(new PdfDictionary(), cffBytesOverride ?? CffBytes));
        doc.AddObject(11, 0, new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("CyrillicCffFixture"),
            [N("Flags")] = new PdfInteger(flags),
            [N("FontFile3")] = Ref(12),
        });

        // /Widths matching the BUILT-IN glyph's advance exactly at code 208; everything else zero
        // (a legal-but-unusable advance — WidthPrecedenceTests' documented fallthrough) since only
        // code 208 is ever shown in this fixture's content stream.
        var widths = new PdfObject[255 - 32 + 1];
        for (var i = 0; i < widths.Length; i++) widths[i] = new PdfInteger(0);
        widths[Code - 32] = new PdfInteger(CustomAdvance);

        var fontDict = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type1"),
            [N("BaseFont")] = N("CyrillicCffFixture"),
            [N("FirstChar")] = new PdfInteger(32),
            [N("LastChar")] = new PdfInteger(255),
            [N("Widths")] = new PdfArray(widths),
            [N("FontDescriptor")] = Ref(11),
        };
        if (encoding is not null)
            fontDict[N("Encoding")] = encoding;
        doc.AddObject(10, 0, fontDict);

        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F0 12 Tf <D0> Tj ET")));
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(4),
            [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = Ref(10) } },
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    private static PdfFont FontFrom(PdfDocument doc)
    {
        var dict = (PdfDictionary)doc.Objects[10];
        PdfFont? font = PdfFont.Create(dict, doc);
        Assert.NotNull(font);
        return font!;
    }

    [Fact]
    public void Symbolic_cff_differences_only_encoding_resolves_through_built_in()
    {
        // ISO 32000-1 §9.6.6.2: a symbolic font's implicit base encoding is the program's own.
        // Pre-fix: StandardEncoding's Annex D.2 band named 208 "emdash" and the whole chain
        // (width rule, renderer, extraction) resolved the WRONG Latin glyph (issue 28 review,
        // CC-MAIN 2000_2000078.pdf).
        using PdfDocument doc = BuildDoc(flags: 6, DifferencesOnlyEncoding());
        PdfFont font = FontFrom(doc);

        Assert.Equal("afii10034", font.Encoding!.GetGlyphName(208));
        Assert.Equal("sterling", font.Encoding.GetGlyphName(127)); // /Differences still wins
    }

    [Fact]
    public void Symbolic_cff_with_correct_built_in_widths_produces_no_width_finding()
    {
        // Declared /Widths match the BUILT-IN glyphs exactly; only the wrong-name path diverges.
        using PdfDocument doc = BuildDoc(flags: 6, DifferencesOnlyEncoding());
        Finding[] findings = new FontProgramRule()
            .Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)).ToArray();
        Assert.DoesNotContain(findings, f => ParitySnapshot.ClauseKey(f.Clause) == "6.2.11.5");
    }

    [Fact]
    public void Explicit_base_encoding_name_still_overrides_the_built_in()
    {
        // Same font, /Encoding = /WinAnsiEncoding NAME: producer override wins over symbolic base.
        //
        // `GetGlyphName(39) == "quotesingle"` alone would be VACUOUS here: WinAnsi 39 and
        // StandardEncoding's ASCII-fallback both land on "quotesingle" via PdfFontEncoding's
        // ASCII-name synthesis switch (32-126), so that assertion alone cannot distinguish "the
        // override won" from "the built-in base silently applied to this arm too and 39 just
        // happens to agree". Code 208 is the code that actually tells the two bases apart: WinAnsi
        // 208 is 0xD0 = 'Ð' (LATIN CAPITAL LETTER ETH) -> AGL name "Eth"; the built-in fixture names
        // 208 "afii10034"; StandardEncoding names it "emdash". Only WinAnsi produces "Eth", so this
        // assertion can only pass if the NAME arm truly used WinAnsi, not the symbolic built-in base.
        using PdfDocument doc = BuildDoc(flags: 6, N("WinAnsiEncoding"));
        PdfFont font = FontFrom(doc);
        Assert.Equal("quotesingle", font.Encoding!.GetGlyphName(39));
        Assert.Equal("Eth", font.Encoding.GetGlyphName(208));
    }

    [Fact]
    public void Non_symbolic_type1_keeps_standard_encoding_base()
    {
        // /Flags 34 (Serif|Nonsymbolic): behaviour is exactly what Tasks 2-3 pinned — no movement.
        using PdfDocument doc = BuildDoc(flags: 34, DifferencesOnlyEncoding());
        PdfFont font = FontFrom(doc);
        Assert.Equal("emdash", font.Encoding!.GetGlyphName(208));
    }

    [Fact]
    public void Symbolic_cff_no_encoding_key_resolves_through_built_in()
    {
        // The other /Encoding-ABSENT arm of LoadEncoding (Type1Font.cs:266-272): BuildDoc's
        // `encoding: null` omits the /Encoding key from the font dict entirely (not merely an
        // empty dict), so this exercises the `!_dictionary.TryGetValue(...)` branch directly —
        // symbolic base is the font program's own built-in encoding, same as the
        // Differences-only-dict tests above but with no dict at all in play.
        using PdfDocument doc = BuildDoc(flags: 6, encoding: null);
        PdfFont font = FontFrom(doc);

        Assert.Equal("afii10034", font.Encoding!.GetGlyphName(208));
    }

    [Fact]
    public void Non_symbolic_no_encoding_key_keeps_standard_encoding()
    {
        // Same absent-/Encoding-key arm, non-symbolic: falls through to
        // GetStandardEncoding(BaseFont), exactly as Tasks 2-3 pinned for the dict-shaped case.
        using PdfDocument doc = BuildDoc(flags: 34, encoding: null);
        PdfFont font = FontFrom(doc);

        Assert.Equal("emdash", font.Encoding!.GetGlyphName(208));
    }

    [Fact]
    public void Explicit_base_encoding_dict_with_differences_overrides_built_in_and_layers_on_top()
    {
        // Composed corner: an /Encoding dict carrying BOTH an explicit /BaseEncoding name AND
        // /Differences. FromDictionary must honor /BaseEncoding over the symbolic built-in base
        // (same override rule as the bare-NAME case above) while still layering /Differences on
        // top of that base, not on top of the built-in encoding it overrode.
        var dict = new PdfDictionary
        {
            [N("BaseEncoding")] = N("WinAnsiEncoding"),
            [N("Differences")] = new PdfArray(new PdfInteger(127), new PdfName("sterling")),
        };
        using PdfDocument doc = BuildDoc(flags: 6, dict);
        PdfFont font = FontFrom(doc);

        Assert.Equal("Eth", font.Encoding!.GetGlyphName(208));       // WinAnsi wins over built-in
        Assert.Equal("sterling", font.Encoding.GetGlyphName(127));   // /Differences still on top
    }

    [Fact]
    public void Symbolic_cff_with_no_built_in_encoding_falls_back_to_standard_encoding()
    {
        // The `any == false` path in TryBuildBuiltInEncoding: a symbolic CFF program that PARSES
        // (IsValid) but carries NO parseable built-in Encoding — format byte 0xFF, the convention
        // MinimalType1CFont (and the shared CffTestFixtures.MinimalCff it mirrors) uses for "no
        // Encoding parsed". GetGlyphIdByCffEncoding then returns 0 for every code, so
        // TryBuildBuiltInEncoding's 0-255 loop never calls SetCharacterName, `any` stays false, and
        // the method returns null — LoadEncoding's `?? GetStandardEncoding(BaseFont)` fallback must
        // then govern, exactly as for a non-symbolic font or one with no embedded program at all.
        // This is the widest-blast-radius branch of the fix: most symbolic CFF fonts in a corpus DO
        // carry a built-in encoding (the CC-MAIN reproducer shape), but any that don't must not
        // regress to a wiped-out encoding — pre-Task-8 accessor-level tests covered
        // GetCffGlyphNameByCharCode returning null in isolation, not this document-level fallback.
        byte[] noEncodingCff = MinimalType1CFont.Build(widthA: 600, widthZ: 300);
        using PdfDocument doc = BuildDoc(flags: 6, DifferencesOnlyEncoding(), cffBytesOverride: noEncodingCff);
        PdfFont font = FontFrom(doc);

        Assert.Equal("emdash", font.Encoding!.GetGlyphName(208));   // StandardEncoding backfill, not left null
        Assert.Equal("sterling", font.Encoding.GetGlyphName(127)); // /Differences still applies on top
    }
}
