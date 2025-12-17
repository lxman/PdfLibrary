using ImageLibrary.Tga;
using Xunit;

namespace TgaCodec.Tests;

public class TgaRoundtripTests
{
    [Fact]
    public void RoundTrip_24Bit_PreservesPixels()
    {
        var original = new TgaImage(4, 4);
        original.SetPixel(0, 0, 255, 0, 0);      // Red
        original.SetPixel(1, 0, 0, 255, 0);      // Green
        original.SetPixel(2, 0, 0, 0, 255);      // Blue
        original.SetPixel(3, 0, 255, 255, 255);  // White
        original.SetPixel(0, 1, 0, 0, 0);        // Black
        original.SetPixel(1, 1, 128, 128, 128);  // Gray

        byte[] encoded = TgaEncoder.Encode(original, 24);
        TgaImage decoded = TgaDecoder.Decode(encoded);

        Assert.Equal(original.Width, decoded.Width);
        Assert.Equal(original.Height, decoded.Height);

        Assert.Equal((255, 0, 0, 255), decoded.GetPixel(0, 0));
        Assert.Equal((0, 255, 0, 255), decoded.GetPixel(1, 0));
        Assert.Equal((0, 0, 255, 255), decoded.GetPixel(2, 0));
        Assert.Equal((255, 255, 255, 255), decoded.GetPixel(3, 0));
        Assert.Equal((0, 0, 0, 255), decoded.GetPixel(0, 1));
        Assert.Equal((128, 128, 128, 255), decoded.GetPixel(1, 1));
    }

    [Fact]
    public void RoundTrip_32Bit_PreservesPixels()
    {
        var original = new TgaImage(3, 3);
        original.SetPixel(0, 0, 255, 0, 0);
        original.SetPixel(1, 1, 0, 255, 0, 128);
        original.SetPixel(2, 2, 0, 0, 255, 64);

        byte[] encoded = TgaEncoder.Encode(original);
        TgaImage decoded = TgaDecoder.Decode(encoded);

        Assert.Equal(original.Width, decoded.Width);
        Assert.Equal(original.Height, decoded.Height);
        Assert.Equal((255, 0, 0, 255), decoded.GetPixel(0, 0));
        Assert.Equal((0, 255, 0, 128), decoded.GetPixel(1, 1));
        Assert.Equal((0, 0, 255, 64), decoded.GetPixel(2, 2));
    }

    [Fact]
    public void RoundTrip_32BitRle_PreservesPixels()
    {
        var original = new TgaImage(3, 3);
        original.SetPixel(0, 0, 255, 0, 0);
        original.SetPixel(1, 1, 0, 255, 0, 128);
        original.SetPixel(2, 2, 0, 0, 255, 64);

        byte[] encoded = TgaEncoder.Encode(original, useRle: true);
        TgaImage decoded = TgaDecoder.Decode(encoded);

        Assert.Equal(original.Width, decoded.Width);
        Assert.Equal(original.Height, decoded.Height);
        Assert.Equal((255, 0, 0, 255), decoded.GetPixel(0, 0));
        Assert.Equal((0, 255, 0, 128), decoded.GetPixel(1, 1));
        Assert.Equal((0, 0, 255, 64), decoded.GetPixel(2, 2));
    }

    [Fact]
    public void RoundTrip_24BitRle_PreservesPixels()
    {
        var original = new TgaImage(4, 4);
        original.SetPixel(0, 0, 255, 0, 0);
        original.SetPixel(1, 0, 0, 255, 0);
        original.SetPixel(2, 0, 0, 0, 255);
        original.SetPixel(3, 0, 255, 255, 255);

        byte[] encoded = TgaEncoder.Encode(original, 24, useRle: true);
        TgaImage decoded = TgaDecoder.Decode(encoded);

        Assert.Equal(original.Width, decoded.Width);
        Assert.Equal(original.Height, decoded.Height);
        Assert.Equal((255, 0, 0, 255), decoded.GetPixel(0, 0));
        Assert.Equal((0, 255, 0, 255), decoded.GetPixel(1, 0));
        Assert.Equal((0, 0, 255, 255), decoded.GetPixel(2, 0));
        Assert.Equal((255, 255, 255, 255), decoded.GetPixel(3, 0));
    }

    [Fact]
    public void RoundTrip_Rle_SolidColor_Compresses()
    {
        // Create a solid color image - should compress well
        var original = new TgaImage(100, 100);
        for (var y = 0; y < 100; y++)
        {
            for (var x = 0; x < 100; x++)
            {
                original.SetPixel(x, y, 128, 64, 32);
            }
        }

        byte[] uncompressed = TgaEncoder.Encode(original);
        byte[] compressed = TgaEncoder.Encode(original, useRle: true);

        // RLE should be much smaller for solid color
        Assert.True(compressed.Length < uncompressed.Length / 2);

        // Verify roundtrip
        TgaImage decoded = TgaDecoder.Decode(compressed);
        Assert.Equal(100, decoded.Width);
        Assert.Equal(100, decoded.Height);
        Assert.Equal((128, 64, 32, 255), decoded.GetPixel(50, 50));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(7, 3)]
    [InlineData(100, 100)]
    public void RoundTrip_VariousSizes_Works(int width, int height)
    {
        var original = new TgaImage(width, height);

        // Fill with gradient
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var r = (byte)(x * 255 / Math.Max(1, width - 1));
                var g = (byte)(y * 255 / Math.Max(1, height - 1));
                var b = (byte)((x + y) * 127 / Math.Max(1, width + height - 2));
                original.SetPixel(x, y, r, g, b);
            }
        }

        byte[] encoded = TgaEncoder.Encode(original, 24);
        TgaImage decoded = TgaDecoder.Decode(encoded);

        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                (byte R, byte G, byte B, byte A) expected = original.GetPixel(x, y);
                (byte R, byte G, byte B, byte A) actual = decoded.GetPixel(x, y);
                Assert.Equal(expected.R, actual.R);
                Assert.Equal(expected.G, actual.G);
                Assert.Equal(expected.B, actual.B);
            }
        }
    }
}

public class TgaHeaderTests
{
    [Fact]
    public void Decode_TooSmall_Throws()
    {
        var data = new byte[10];
        Assert.Throws<TgaException>(() => TgaDecoder.Decode(data));
    }

    [Fact]
    public void Encode_InvalidBitsPerPixel_Throws()
    {
        var image = new TgaImage(10, 10);
        Assert.Throws<ArgumentException>(() => TgaEncoder.Encode(image, 16));
    }
}

public class TgaImageTests
{
    [Fact]
    public void Constructor_InvalidWidth_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TgaImage(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TgaImage(-1, 10));
    }

    [Fact]
    public void Constructor_InvalidHeight_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TgaImage(10, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TgaImage(10, -1));
    }

    [Fact]
    public void GetPixel_OutOfRange_Throws()
    {
        var image = new TgaImage(10, 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => image.GetPixel(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.GetPixel(10, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.GetPixel(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.GetPixel(0, 10));
    }

    [Fact]
    public void SetPixel_OutOfRange_Throws()
    {
        var image = new TgaImage(10, 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => image.SetPixel(-1, 0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.SetPixel(10, 0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.SetPixel(0, -1, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.SetPixel(0, 10, 0, 0, 0));
    }

    [Fact]
    public void SetPixel_GetPixel_RoundTrip()
    {
        var image = new TgaImage(10, 10);

        image.SetPixel(5, 5, 100, 150, 200);
        (byte R, byte G, byte B, byte A) pixel = image.GetPixel(5, 5);

        Assert.Equal(100, pixel.R);
        Assert.Equal(150, pixel.G);
        Assert.Equal(200, pixel.B);
        Assert.Equal(255, pixel.A);
    }

    [Fact]
    public void NewImage_DefaultsToBlack()
    {
        var image = new TgaImage(5, 5);

        (byte R, byte G, byte B, byte A) pixel = image.GetPixel(2, 2);
        Assert.Equal(0, pixel.R);
        Assert.Equal(0, pixel.G);
        Assert.Equal(0, pixel.B);
        Assert.Equal(0, pixel.A);
    }
}
