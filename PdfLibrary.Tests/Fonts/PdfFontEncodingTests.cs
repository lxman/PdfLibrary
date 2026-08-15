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
}
