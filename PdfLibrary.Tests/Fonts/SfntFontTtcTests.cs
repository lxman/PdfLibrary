using FontParser;

namespace PdfLibrary.Tests.Fonts;

public class SfntFontTtcTests
{
    private static byte[] BareFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

    // Builds a VALID 1-font classic TTC around a bare sfnt font: a 16-byte TTC header, then the
    // font — with each table-directory offset shifted by the header size, because real .ttc table
    // offsets are absolute from the FILE start (the parser must NOT add the font's base offset).
    private static byte[] WrapAsTtc(byte[] font)
    {
        const int headerSize = 16;
        byte[] header =
        {
            0x74, 0x74, 0x63, 0x66, // 'ttcf'
            0x00, 0x01,             // majorVersion
            0x00, 0x00,             // minorVersion
            0x00, 0x00, 0x00, 0x01, // numFonts = 1
            0x00, 0x00, 0x00, 0x10  // offset[0] = 16
        };
        var ttc = new byte[headerSize + font.Length];
        Array.Copy(header, 0, ttc, 0, headerSize);
        Array.Copy(font, 0, ttc, headerSize, font.Length);

        // sfnt directory at ttc[headerSize]: sfntVersion(4) numTables(2) searchRange(2)
        // entrySelector(2) rangeShift(2) = 12 bytes, then 16-byte records (tag4, checksum4, offset4, length4).
        int numTables = (ttc[headerSize + 4] << 8) | ttc[headerSize + 5];
        int firstRecord = headerSize + 12;
        for (int i = 0; i < numTables; i++)
        {
            int offPos = firstRecord + i * 16 + 8; // skip tag(4) + checksum(4)
            uint off = ((uint)ttc[offPos] << 24) | ((uint)ttc[offPos + 1] << 16)
                     | ((uint)ttc[offPos + 2] << 8) | ttc[offPos + 3];
            off += headerSize;
            ttc[offPos]     = (byte)(off >> 24);
            ttc[offPos + 1] = (byte)(off >> 16);
            ttc[offPos + 2] = (byte)(off >> 8);
            ttc[offPos + 3] = (byte)off;
        }
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
