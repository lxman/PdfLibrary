using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using Xunit;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// Regression (2026-07-06 PdfLibrary smoke): the ISO 32000-1 footer's copyright sign (WinAnsi 0xA9 in
/// an embedded Type1C subset) and en dash (0x96) extracted correctly but did not RENDER. The
/// standard-encoding factories populated only the code→Unicode table, so
/// <see cref="PdfFontEncoding.GetGlyphName"/> returned null for every code ≥ 127 and the
/// renderer's name-based CFF charstring lookup resolved to .notdef. GetGlyphName must return the
/// Adobe Glyph List name for any code whose Unicode is known.
/// </summary>
public class PdfFontEncodingTests
{
    [Theory]
    [InlineData(0xA9, "copyright")]   // © — the smoke footer glyph
    [InlineData(0x96, "endash")]      // – between "2008" and "All rights reserved"
    [InlineData(0xE9, "eacute")]      // é — representative accented Latin-1
    [InlineData(0x80, "Euro")]        // € — CP1252 block (128–159)
    [InlineData(0x95, "bullet")]      // • — CP1252 block
    public void WinAnsi_GetGlyphName_ResolvesHighCodes(int code, string expected)
    {
        PdfFontEncoding enc = PdfFontEncoding.GetStandardEncoding("WinAnsiEncoding");
        Assert.Equal(expected, enc.GetGlyphName(code));
    }

    [Fact]
    public void WinAnsi_GetGlyphName_AsciiStillResolves()
    {
        PdfFontEncoding enc = PdfFontEncoding.GetStandardEncoding("WinAnsiEncoding");
        Assert.Equal("one", enc.GetGlyphName('1'));
        Assert.Equal("A", enc.GetGlyphName('A'));
        Assert.Equal("space", enc.GetGlyphName(' '));
    }

    [Fact]
    public void WinAnsi_DecodeCharacter_HighCodes_Unchanged()
    {
        PdfFontEncoding enc = PdfFontEncoding.GetStandardEncoding("WinAnsiEncoding");
        Assert.Equal("©", enc.DecodeCharacter(0xA9));
        Assert.Equal("–", enc.DecodeCharacter(0x96));
    }

    // ── Issue 25: StandardEncoding must carry Annex D.2 names, not WinAnsi ones ──────────────
    // ISO 32000-2 Annex D.2 StandardEncoding differs from ASCII/WinAnsi at exactly two codes:
    // 39 is quoteright (U+2019), not quotesingle; 96 is quoteleft (U+2018), not grave.
    // Reproducer: "Postscript Language Reference Manual.pdf" — Minion-Regular with implicit
    // StandardEncoding drew gid 104 (quotesingle) where quoteright is gid 8, and extraction gave
    // U+0027/U+0060 where pdftotext gives U+2019/U+2018.

    [Theory]
    [InlineData(39, "quoteright", "\u2019")]
    [InlineData(96, "quoteleft", "\u2018")]
    public void Standard_encoding_quote_codes_carry_annex_d_names(int code, string name, string unicode)
    {
        PdfFontEncoding encoding = PdfFontEncoding.GetStandardEncoding("StandardEncoding");
        Assert.Equal(name, encoding.GetGlyphName(code));
        Assert.Equal(unicode, encoding.DecodeCharacter(code));
    }

    [Theory]
    [InlineData(32, "space", " ")]
    [InlineData(48, "zero", "0")]
    [InlineData(65, "A", "A")]
    [InlineData(122, "z", "z")]
    [InlineData(126, "asciitilde", "~")]
    public void Standard_encoding_other_ascii_codes_are_unchanged(int code, string name, string unicode)
    {
        PdfFontEncoding encoding = PdfFontEncoding.GetStandardEncoding("StandardEncoding");
        Assert.Equal(name, encoding.GetGlyphName(code));
        Assert.Equal(unicode, encoding.DecodeCharacter(code));
    }

    [Theory]
    [InlineData(39, "quotesingle", "'")]
    [InlineData(96, "grave", "`")]
    public void Win_ansi_quote_codes_are_untouched(int code, string name, string unicode)
    {
        // The fix must not "correct" the encoding that was already right (spec gate 3).
        PdfFontEncoding encoding = PdfFontEncoding.GetStandardEncoding("WinAnsiEncoding");
        Assert.Equal(name, encoding.GetGlyphName(code));
        Assert.Equal(unicode, encoding.DecodeCharacter(code));
    }

    [Fact]
    public void Dictionary_without_base_encoding_defaults_to_annex_d_standard_encoding()
    {
        // FromDictionary's no-/BaseEncoding fallback is how the reproducer's font reaches
        // StandardEncoding (obj 17979: /Differences only, no /BaseEncoding).
        PdfFontEncoding encoding = PdfFontEncoding.FromDictionary(new PdfDictionary());
        Assert.Equal("quoteright", encoding.GetGlyphName(39));
        Assert.Equal("quoteleft", encoding.GetGlyphName(96));
    }

    [Fact]
    public void Unknown_encoding_name_defaults_to_annex_d_standard_encoding()
    {
        // GetStandardEncoding's `_ =>` catch-all arm — reached for unknown /BaseEncoding names.
        PdfFontEncoding encoding = PdfFontEncoding.GetStandardEncoding("NoSuchEncoding");
        Assert.Equal("quoteright", encoding.GetGlyphName(39));
    }

    // ── Issue 28: Annex D.2's upper band (accents 193-208, letters 225-251) ──────────────────
    // Previously unpopulated: DecodeCharacter fell to Latin-1 (code 193 → "Á" instead of grave),
    // the substitute-render path drew that Latin-1 LETTER where a bare accent MARK belongs
    // (reproducer: PLRM page 789's Times specimen accent row, poppler-confirmed 2026-08-15), and
    // Standard14Metrics.WidthByCode misread the band as WinAnsi codes.
    [Theory]
    [InlineData(193, "grave", "`")]
    [InlineData(194, "acute", "´")]
    [InlineData(195, "circumflex", "ˆ")]
    [InlineData(196, "tilde", "˜")]
    [InlineData(197, "macron", "¯")]
    [InlineData(198, "breve", "˘")]
    [InlineData(199, "dotaccent", "˙")]
    [InlineData(200, "dieresis", "¨")]
    [InlineData(202, "ring", "˚")]
    [InlineData(203, "cedilla", "¸")]
    [InlineData(205, "hungarumlaut", "˝")]
    [InlineData(206, "ogonek", "˛")]
    [InlineData(207, "caron", "ˇ")]
    [InlineData(208, "emdash", "—")]
    [InlineData(225, "AE", "Æ")]
    [InlineData(227, "ordfeminine", "ª")]
    [InlineData(232, "Lslash", "Ł")]
    [InlineData(233, "Oslash", "Ø")]
    [InlineData(234, "OE", "Œ")]
    [InlineData(235, "ordmasculine", "º")]
    [InlineData(241, "ae", "æ")]
    [InlineData(245, "dotlessi", "ı")]
    [InlineData(248, "lslash", "ł")]
    [InlineData(249, "oslash", "ø")]
    [InlineData(250, "oe", "œ")]
    [InlineData(251, "germandbls", "ß")]
    public void Standard_encoding_upper_band_carries_annex_d_names(int code, string name, string unicode)
    {
        PdfFontEncoding encoding = PdfFontEncoding.GetStandardEncoding("StandardEncoding");
        Assert.Equal(name, encoding.GetGlyphName(code));
        Assert.Equal(unicode, encoding.DecodeCharacter(code));
    }

    [Theory]
    [InlineData(192)]
    [InlineData(209)]
    [InlineData(255)]
    public void Standard_encoding_unassigned_upper_codes_stay_unnamed(int code)
    {
        // Annex D.2 leaves these blank; they keep the (documented) Latin-1 extraction fallback.
        PdfFontEncoding encoding = PdfFontEncoding.GetStandardEncoding("StandardEncoding");
        Assert.Null(encoding.GetGlyphName(code));
    }

    [Fact]
    public void Win_ansi_upper_band_is_untouched_and_accents_gain_names()
    {
        PdfFontEncoding encoding = PdfFontEncoding.GetStandardEncoding("WinAnsiEncoding");
        // 193 is Aacute in WinAnsi — the fix must not leak Standard's table sideways.
        Assert.Equal("Aacute", encoding.GetGlyphName(193));
        Assert.Equal("Á", encoding.DecodeCharacter(193));
        // Deliberate GlyphList side effect: 0x88/0x98 previously derived NO name (the gap
        // Standard14Metrics.CodeAliases works around); now they derive the accent names.
        Assert.Equal("circumflex", encoding.GetGlyphName(0x88));
        Assert.Equal("tilde", encoding.GetGlyphName(0x98));
    }

    // ── Issue 27: FromDictionary must honor its baseEncoding argument ─────────────────────────
    // The parameter was dead: a /Differences-only dict always got StandardEncoding, discarding
    // TrueTypeFont's WinAnsi intent and Type1Font's Symbol/ZapfDingbats gate. Zero corpus
    // occurrences of the TrueType shape exist in local-708 (probe 2026-08-15: all 12 TrueType
    // /Differences dicts carry explicit /BaseEncoding), so these synthetic gates are the net.
    [Fact]
    public void Dictionary_without_base_encoding_honors_the_callers_base()
    {
        PdfFontEncoding encoding = PdfFontEncoding.FromDictionary(
            new PdfDictionary(), PdfFontEncoding.GetStandardEncoding("WinAnsiEncoding"));
        Assert.Equal("quotesingle", encoding.GetGlyphName(39));   // WinAnsi, not Standard's quoteright
        Assert.Equal("quoteright", encoding.GetGlyphName(0x92));  // WinAnsi smart quote
    }

    [Fact]
    public void Explicit_base_encoding_in_the_dictionary_beats_the_parameter()
    {
        var dict = new PdfDictionary { [new PdfName("BaseEncoding")] = new PdfName("SymbolEncoding") };
        PdfFontEncoding encoding = PdfFontEncoding.FromDictionary(
            dict, PdfFontEncoding.GetStandardEncoding("WinAnsiEncoding"));
        Assert.Equal("Alpha", encoding.GetGlyphName(65)); // Symbol won; parameter lost
    }

    [Fact]
    public void True_type_differences_only_dictionary_resolves_through_win_ansi()
    {
        // The wiring, end to end: TrueTypeFont.LoadEncoding passes WinAnsi as the intended base
        // for a /Differences-only dict (TrueTypeFont.cs:150-151); pre-fix it was discarded.
        var font = PdfFont.Create(new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName("TrueType"),
            [new PdfName("BaseFont")] = new PdfName("Test"),
            [new PdfName("Encoding")] = new PdfDictionary
            {
                [new PdfName("Differences")] = new PdfArray(new PdfInteger(65), new PdfName("Alpha")),
            },
        });
        Assert.NotNull(font);
        Assert.Equal("Alpha", font!.Encoding!.GetGlyphName(65));       // the difference still wins
        Assert.Equal("quotesingle", font.Encoding.GetGlyphName(39));   // WinAnsi base honored
    }

    [Fact]
    public void Symbol_type1_differences_only_dictionary_resolves_through_symbol_encoding()
    {
        // Type1Font's name-based Symbol gate (Type1Font.cs:314-319) was also being discarded.
        var font = PdfFont.Create(new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName("Type1"),
            [new PdfName("BaseFont")] = new PdfName("Symbol"),
            [new PdfName("Encoding")] = new PdfDictionary
            {
                [new PdfName("Differences")] = new PdfArray(new PdfInteger(32), new PdfName("space")),
            },
        });
        Assert.NotNull(font);
        Assert.Equal("suchthat", font!.Encoding!.GetGlyphName(39)); // SymbolEncoding 39, not quotes
    }

    // ── Task 6: WinAnsi ASCII names must be document-asserted, not derived ───────────────────
    // CreateWinAnsiEncoding built codes 32-126 via SetUnicode, which marks every name DERIVED
    // (a reverse-AGL reconstruction). FontProgramRule.ResolveSimpleGlyph refuses to call a glyph
    // absent from the embedded font program when its name is derived, so this suppressed two
    // genuine corpus detections (veraPDF 6-2-11-4-1-t02-fail-a/-b). A name from the Annex D.2
    // WinAnsiEncoding table the document itself named via /Encoding is asserted BY the document.

    [Fact]
    public void WinAnsi_ascii_names_are_document_asserted_not_derived()
    {
        PdfFontEncoding enc = PdfFontEncoding.GetStandardEncoding("WinAnsiEncoding");

        // period and numbersign are the two the corpus fixtures turn on.
        Assert.Equal("period", enc.GetGlyphName(46));
        Assert.Equal("numbersign", enc.GetGlyphName(35));
        Assert.False(enc.IsDerivedName(46));
        Assert.False(enc.IsDerivedName(35));
    }

    [Fact]
    public void WinAnsi_differs_from_standard_at_exactly_two_ascii_codes()
    {
        PdfFontEncoding win = PdfFontEncoding.GetStandardEncoding("WinAnsiEncoding");
        PdfFontEncoding std = PdfFontEncoding.GetStandardEncoding("StandardEncoding");

        Assert.Equal("quotesingle", win.GetGlyphName(39));
        Assert.Equal("quoteright", std.GetGlyphName(39));
        Assert.Equal("grave", win.GetGlyphName(96));
        Assert.Equal("quoteleft", std.GetGlyphName(96));

        // Every other ASCII code agrees — this is what lets WinAnsi reuse the Standard table.
        for (var code = 32; code <= 126; code++)
        {
            if (code is 39 or 96) continue;
            Assert.Equal(std.GetGlyphName(code), win.GetGlyphName(code));
        }
    }
}
