using PdfLibrary.Fonts;
using Xunit;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// Regression (2026-07-06 Focal smoke): the ISO 32000-1 footer's copyright sign (WinAnsi 0xA9 in
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
}
