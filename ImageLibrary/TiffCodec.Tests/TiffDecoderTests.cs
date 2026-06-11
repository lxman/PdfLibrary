using System.IO;
using System.IO.Compression;
using Xunit;

namespace TiffCodec.Tests;

/// <summary>
/// Regression coverage for the TIFF decoder. Each test builds a minimal in-memory TIFF with
/// <see cref="TiffBuilder"/> and checks a specific decode behaviour that was previously wrong or
/// unsupported. Photometric codes: 0 = WhiteIsZero, 1 = BlackIsZero, 2 = RGB, 3 = Palette.
/// Compression codes: 1 = none, 8 = Adobe Deflate.
/// </summary>
public class TiffDecoderTests
{
    private static byte[] Zlib(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    [Fact]
    public void Grayscale8_blackiszero_decodes_directly()
    {
        var b = new TiffBuilder();
        int data = b.AddBlock([0, 85, 170, 255]);
        byte[] tiff = b.Short(TiffBuilder.ImageWidth, 2).Short(TiffBuilder.ImageHeight, 2)
            .Short(TiffBuilder.BitsPerSample, 8).Short(TiffBuilder.Compression, 1)
            .Short(TiffBuilder.Photometric, 1).Short(TiffBuilder.SamplesPerPixel, 1)
            .Short(TiffBuilder.RowsPerStrip, 2).OffsetsOf(TiffBuilder.StripOffsets, data)
            .LengthsOf(TiffBuilder.StripByteCounts, data).Build();

        TiffImage img = TiffDecoder.Decode(tiff);

        Assert.Equal((byte)0, img.GetPixel(0, 0).Red);
        Assert.Equal((byte)85, img.GetPixel(1, 0).Red);
        Assert.Equal((byte)170, img.GetPixel(0, 1).Red);
        Assert.Equal((byte)255, img.GetPixel(1, 1).Red);
    }

    [Fact]
    public void Grayscale8_whiteiszero_is_inverted()
    {
        // Photometric 0 (WhiteIsZero) was unsupported for 8-bit and threw. Sample 0 = white.
        var b = new TiffBuilder();
        int data = b.AddBlock([0, 255]);
        byte[] tiff = b.Short(TiffBuilder.ImageWidth, 2).Short(TiffBuilder.ImageHeight, 1)
            .Short(TiffBuilder.BitsPerSample, 8).Short(TiffBuilder.Compression, 1)
            .Short(TiffBuilder.Photometric, 0).Short(TiffBuilder.SamplesPerPixel, 1)
            .Short(TiffBuilder.RowsPerStrip, 1).OffsetsOf(TiffBuilder.StripOffsets, data)
            .LengthsOf(TiffBuilder.StripByteCounts, data).Build();

        TiffImage img = TiffDecoder.Decode(tiff);

        Assert.Equal((byte)255, img.GetPixel(0, 0).Red); // sample 0 → white
        Assert.Equal((byte)0, img.GetPixel(1, 0).Red);   // sample 255 → black
    }

    [Fact]
    public void Palette_expands_indices_through_the_colormap()
    {
        // ColorMap layout: 256 R entries, then 256 G, then 256 B (each 16-bit). Index 0 = red,
        // index 1 = green. Palette photometric (3) was previously unsupported (threw).
        var colorMap = new ushort[768];
        colorMap[0] = 0xFFFF;       // R[0]
        colorMap[256 + 1] = 0xFFFF; // G[1]

        var b = new TiffBuilder();
        int data = b.AddBlock([0, 1]); // two pixels: index 0, index 1
        byte[] tiff = b.Short(TiffBuilder.ImageWidth, 2).Short(TiffBuilder.ImageHeight, 1)
            .Short(TiffBuilder.BitsPerSample, 8).Short(TiffBuilder.Compression, 1)
            .Short(TiffBuilder.Photometric, 3).Short(TiffBuilder.SamplesPerPixel, 1)
            .Short(TiffBuilder.RowsPerStrip, 1).Short(TiffBuilder.ColorMap, colorMap)
            .OffsetsOf(TiffBuilder.StripOffsets, data).LengthsOf(TiffBuilder.StripByteCounts, data).Build();

        TiffImage img = TiffDecoder.Decode(tiff);

        (byte Blue, byte Green, byte Red, byte Alpha) p0 = img.GetPixel(0, 0);
        (byte Blue, byte Green, byte Red, byte Alpha) p1 = img.GetPixel(1, 0);
        Assert.Equal(((byte)255, (byte)0), (p0.Red, p0.Green)); // red
        Assert.Equal(((byte)0, (byte)255), (p1.Red, p1.Green)); // green
    }

    [Fact]
    public void Grayscale16_uses_high_byte_not_histogram_normalisation()
    {
        // Two 16-bit samples: 0x8000 and 0xFFFF (little-endian). Correct >>8 gives 128 and 255.
        // The old min/max normalisation would stretch 0x8000→0, so asserting 128 proves it's gone.
        var b = new TiffBuilder();
        int data = b.AddBlock([0x00, 0x80, 0xFF, 0xFF]);
        byte[] tiff = b.Short(TiffBuilder.ImageWidth, 2).Short(TiffBuilder.ImageHeight, 1)
            .Short(TiffBuilder.BitsPerSample, 16).Short(TiffBuilder.Compression, 1)
            .Short(TiffBuilder.Photometric, 1).Short(TiffBuilder.SamplesPerPixel, 1)
            .Short(TiffBuilder.RowsPerStrip, 1).OffsetsOf(TiffBuilder.StripOffsets, data)
            .LengthsOf(TiffBuilder.StripByteCounts, data).Build();

        TiffImage img = TiffDecoder.Decode(tiff);

        Assert.Equal((byte)128, img.GetPixel(0, 0).Red);
        Assert.Equal((byte)255, img.GetPixel(1, 0).Red);
    }

    [Fact]
    public void Predictor2_reconstructs_per_component_for_rgb()
    {
        // RGB row [10,20,30, 15,25,35]; predictor-2 encoded = [10,20,30, 5,5,5]. Correct decoding
        // adds the same component of the previous pixel (stride 3). The old byte-wise (stride 1)
        // decode produced garbage. Deflate-compressed so the predictor path runs.
        byte[] encoded = [10, 20, 30, 5, 5, 5];
        var b = new TiffBuilder();
        int data = b.AddBlock(Zlib(encoded));
        byte[] tiff = b.Short(TiffBuilder.ImageWidth, 2).Short(TiffBuilder.ImageHeight, 1)
            .Short(TiffBuilder.BitsPerSample, 8, 8, 8).Short(TiffBuilder.Compression, 8)
            .Short(TiffBuilder.Photometric, 2).Short(TiffBuilder.SamplesPerPixel, 3)
            .Short(TiffBuilder.RowsPerStrip, 1).Short(TiffBuilder.Predictor, 2)
            .OffsetsOf(TiffBuilder.StripOffsets, data).LengthsOf(TiffBuilder.StripByteCounts, data).Build();

        TiffImage img = TiffDecoder.Decode(tiff);

        (byte Blue, byte Green, byte Red, byte Alpha) p0 = img.GetPixel(0, 0);
        (byte Blue, byte Green, byte Red, byte Alpha) p1 = img.GetPixel(1, 0);
        Assert.Equal(((byte)10, (byte)20, (byte)30), (p0.Red, p0.Green, p0.Blue));
        Assert.Equal(((byte)15, (byte)25, (byte)35), (p1.Red, p1.Green, p1.Blue)); // 5+10, 5+20, 5+30
    }

    [Fact]
    public void Tiles_are_placed_in_row_major_order()
    {
        // 4x4 image, four 2x2 tiles, each a solid grey. Row-major order: tile0=TL, 1=TR, 2=BL, 3=BR.
        // The old column-major math put tile2 where tile1 belongs, so the top-right pixel exposes it.
        var b = new TiffBuilder();
        int tl = b.AddBlock([10, 10, 10, 10]);
        int tr = b.AddBlock([20, 20, 20, 20]);
        int bl = b.AddBlock([30, 30, 30, 30]);
        int br = b.AddBlock([40, 40, 40, 40]);
        byte[] tiff = b.Short(TiffBuilder.ImageWidth, 4).Short(TiffBuilder.ImageHeight, 4)
            .Short(TiffBuilder.BitsPerSample, 8).Short(TiffBuilder.Compression, 1)
            .Short(TiffBuilder.Photometric, 1).Short(TiffBuilder.SamplesPerPixel, 1)
            .Short(TiffBuilder.TileWidth, 2).Short(TiffBuilder.TileLength, 2)
            .OffsetsOf(TiffBuilder.TileOffsets, tl, tr, bl, br)
            .LengthsOf(TiffBuilder.TileByteCounts, tl, tr, bl, br).Build();

        TiffImage img = TiffDecoder.Decode(tiff);

        Assert.Equal((byte)10, img.GetPixel(0, 0).Red); // top-left
        Assert.Equal((byte)20, img.GetPixel(3, 0).Red); // top-right (was tile2's 30 under column-major)
        Assert.Equal((byte)30, img.GetPixel(0, 3).Red); // bottom-left
        Assert.Equal((byte)40, img.GetPixel(3, 3).Red); // bottom-right
    }

    [Fact]
    public void Absurd_dimensions_throw_tiffexception_not_overflow()
    {
        // width*height*4 overflows int in the old `new byte[...]`; the guard computes in long and
        // throws a clean TiffException. ImageWidth/Height are LONG (valid per TIFF).
        var b = new TiffBuilder();
        int data = b.AddBlock([0, 0, 0, 0]);
        byte[] tiff = b.Long(TiffBuilder.ImageWidth, 100000).Long(TiffBuilder.ImageHeight, 100000)
            .Short(TiffBuilder.BitsPerSample, 8).Short(TiffBuilder.Compression, 1)
            .Short(TiffBuilder.Photometric, 1).Short(TiffBuilder.SamplesPerPixel, 1)
            .Short(TiffBuilder.RowsPerStrip, 1).OffsetsOf(TiffBuilder.StripOffsets, data)
            .LengthsOf(TiffBuilder.StripByteCounts, data).Build();

        Assert.Throws<TiffException>(() => TiffDecoder.Decode(tiff));
    }
}
