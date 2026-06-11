using System;
using System.Linq;
using System.Text;
using Xunit;

namespace PbmCodec.Tests;

/// <summary>
/// Coverage for the Netpbm decoder (it previously had no test project): the six P1-P6 variants,
/// maxval scaling, 16-bit samples, comments, and the dimension / header-overflow guards.
/// </summary>
public class PbmDecoderTests
{
    // ASCII header text followed by an optional raw binary raster.
    private static byte[] Build(string header, params byte[] raster) =>
        Encoding.ASCII.GetBytes(header).Concat(raster).ToArray();

    [Fact]
    public void P1_ascii_bitmap_black_is_one()
    {
        PbmImage img = PbmDecoder.Decode(Build("P1\n2 1\n1 0\n"));
        Assert.Equal((byte)0, img.GetPixel(0, 0).R);    // sample 1 = black
        Assert.Equal((byte)255, img.GetPixel(1, 0).R);  // sample 0 = white
    }

    [Fact]
    public void P4_binary_bitmap_unpacks_msb_first()
    {
        // One row of 8 pixels, byte 0x80 = leftmost bit set (black), rest clear (white).
        PbmImage img = PbmDecoder.Decode(Build("P4\n8 1\n", 0x80));
        Assert.Equal((byte)0, img.GetPixel(0, 0).R);
        Assert.Equal((byte)255, img.GetPixel(7, 0).R);
    }

    [Fact]
    public void P5_binary_graymap_reads_samples()
    {
        PbmImage img = PbmDecoder.Decode(Build("P5\n2 1\n255\n", 0, 128));
        Assert.Equal((byte)0, img.GetPixel(0, 0).R);
        Assert.Equal((byte)128, img.GetPixel(1, 0).R);
    }

    [Fact]
    public void P6_binary_pixmap_reads_rgb()
    {
        PbmImage img = PbmDecoder.Decode(Build("P6\n1 1\n255\n", 255, 0, 0));
        (byte R, byte G, byte B, byte A) p = img.GetPixel(0, 0);
        Assert.Equal(((byte)255, (byte)0, (byte)0), (p.R, p.G, p.B));
    }

    [Fact]
    public void P3_ascii_pixmap_reads_rgb()
    {
        PbmImage img = PbmDecoder.Decode(Build("P3\n1 1\n255\n0 255 0\n"));
        (byte R, byte G, byte B, byte A) p = img.GetPixel(0, 0);
        Assert.Equal(((byte)0, (byte)255, (byte)0), (p.R, p.G, p.B));
    }

    [Fact]
    public void Maxval_below_255_is_scaled_up()
    {
        // maxval 15: sample 15 scales to 255, sample 0 stays 0.
        PbmImage img = PbmDecoder.Decode(Build("P5\n2 1\n15\n", 15, 0));
        Assert.Equal((byte)255, img.GetPixel(0, 0).R);
        Assert.Equal((byte)0, img.GetPixel(1, 0).R);
    }

    [Fact]
    public void Sixteen_bit_graymap_reads_big_endian_samples()
    {
        // maxval 65535: two-byte big-endian samples. 0xFFFF -> 255, 0x8000 -> ~128.
        PbmImage img = PbmDecoder.Decode(Build("P5\n2 1\n65535\n", 0xFF, 0xFF, 0x80, 0x00));
        Assert.Equal((byte)255, img.GetPixel(0, 0).R);
        Assert.Equal((byte)128, img.GetPixel(1, 0).R);
    }

    [Fact]
    public void Comments_in_header_are_skipped()
    {
        PbmImage img = PbmDecoder.Decode(Build("P2\n# a comment\n2 1\n255\n# another\n0 255\n"));
        Assert.Equal((byte)0, img.GetPixel(0, 0).R);
        Assert.Equal((byte)255, img.GetPixel(1, 0).R);
    }

    [Fact]
    public void Oversize_dimensions_throw()
    {
        // 100000*100000*4 overflows int; the guard computes in long and throws PbmException.
        Assert.Throws<PbmException>(() => PbmDecoder.Decode(Build("P5\n100000 100000\n255\n")));
    }

    [Fact]
    public void Header_integer_overflow_throws()
    {
        // A width that overflows int must throw cleanly rather than wrap.
        Assert.Throws<PbmException>(() => PbmDecoder.Decode(Build("P5\n99999999999 1\n255\n")));
    }

    [Fact]
    public void Truncated_raster_throws()
    {
        // Declares 4 pixels but provides 1 byte.
        Assert.Throws<PbmException>(() => PbmDecoder.Decode(Build("P5\n2 2\n255\n", 0)));
    }
}
