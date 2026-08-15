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
}
