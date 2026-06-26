using FontParser;

namespace PdfLibrary.Tests.Fonts;

public class SfntFontTtcTests
{
    private static byte[] BareFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

    // 16-byte classic TTC header (major=1, minor=0, numFonts=1, offset[0]=16), big-endian.
    private static byte[] WrapAsTtc(byte[] font)
    {
        byte[] header =
        {
            0x74, 0x74, 0x63, 0x66, // 'ttcf'
            0x00, 0x01,             // majorVersion
            0x00, 0x00,             // minorVersion
            0x00, 0x00, 0x00, 0x01, // numFonts = 1
            0x00, 0x00, 0x00, 0x10  // offset[0] = 16 (font 0 starts right after the header)
        };
        var ttc = new byte[header.Length + font.Length];
        Array.Copy(header, 0, ttc, 0, header.Length);
        Array.Copy(font, 0, ttc, header.Length, font.Length);
        return ttc;
    }

    [Fact]
    public void Ttc_ParsesFirstFont_LikeBareFont()
    {
        byte[] bare = BareFont();
        var single = new SfntFont(bare);
        var collection = new SfntFont(WrapAsTtc(bare));

        Assert.Equal(single.NumGlyphs, collection.NumGlyphs);
        Assert.Equal(single.UnitsPerEm, collection.UnitsPerEm);
        Assert.Equal(single.OutlineKind, collection.OutlineKind);
        Assert.True(collection.HasTable("glyf") || collection.HasTable("CFF "));
    }
}
