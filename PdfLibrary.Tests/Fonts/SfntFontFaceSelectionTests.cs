using FontParser;

namespace PdfLibrary.Tests.Fonts;

public class SfntFontFaceSelectionTests
{
    private static byte[] BareFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

    // Build a 2-font TTC where BOTH faces point at the same shared font directory (legal in TTC).
    // Header = ttcf(4) major(2) minor(2) numFonts(4)=2 offset[0](4) offset[1](4) = 20 bytes; both offsets = 20.
    private static byte[] WrapAsTwoFaceTtc(byte[] font)
    {
        const int headerSize = 20;
        var ttc = new byte[headerSize + font.Length];
        // 'ttcf'
        ttc[0] = 0x74; ttc[1] = 0x74; ttc[2] = 0x63; ttc[3] = 0x66;
        ttc[4] = 0x00; ttc[5] = 0x01;            // major
        ttc[6] = 0x00; ttc[7] = 0x00;            // minor
        ttc[8] = 0x00; ttc[9] = 0x00; ttc[10] = 0x00; ttc[11] = 0x02; // numFonts = 2
        ttc[12] = 0x00; ttc[13] = 0x00; ttc[14] = 0x00; ttc[15] = 0x14; // offset[0] = 20
        ttc[16] = 0x00; ttc[17] = 0x00; ttc[18] = 0x00; ttc[19] = 0x14; // offset[1] = 20
        Array.Copy(font, 0, ttc, headerSize, font.Length);

        // Shift each table-directory offset by headerSize (file-absolute, as a real .ttc requires).
        int numTables = (ttc[headerSize + 4] << 8) | ttc[headerSize + 5];
        int firstRecord = headerSize + 12;
        for (int i = 0; i < numTables; i++)
        {
            int offPos = firstRecord + i * 16 + 8;
            uint off = ((uint)ttc[offPos] << 24) | ((uint)ttc[offPos + 1] << 16)
                     | ((uint)ttc[offPos + 2] << 8) | ttc[offPos + 3];
            off += headerSize;
            ttc[offPos] = (byte)(off >> 24); ttc[offPos + 1] = (byte)(off >> 16);
            ttc[offPos + 2] = (byte)(off >> 8); ttc[offPos + 3] = (byte)off;
        }
        return ttc;
    }

    [Fact]
    public void Ttc_FaceCount_AndBothFacesParse()
    {
        byte[] ttc = WrapAsTwoFaceTtc(BareFont());
        var bare = new SfntFont(BareFont());

        var face0 = new SfntFont(ttc, 0);
        var face1 = new SfntFont(ttc, 1);

        Assert.Equal(2, face0.FaceCount);
        Assert.Equal(bare.NumGlyphs, face0.NumGlyphs);
        Assert.Equal(bare.NumGlyphs, face1.NumGlyphs);
    }

    [Fact]
    public void Ttc_FaceIndexOutOfRange_Throws()
    {
        byte[] ttc = WrapAsTwoFaceTtc(BareFont());
        Assert.Throws<ArgumentOutOfRangeException>(() => new SfntFont(ttc, 2));
    }

    [Fact]
    public void SingleFont_FaceCountIsOne_AndOnlyFaceZeroValid()
    {
        var single = new SfntFont(BareFont());
        Assert.Equal(1, single.FaceCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SfntFont(BareFont(), 1));
    }
}
