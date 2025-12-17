using System.Text;
using ImageLibrary.Png;
using Xunit;

namespace PngCodec.Tests;

public class PngRoundtripTests
{
    [Fact]
    public void RoundTrip_Rgba_PreservesPixels()
    {
        var original = new PngImage(4, 4);
        original.SetPixel(0, 0, 255, 0, 0);      // Red
        original.SetPixel(1, 0, 0, 255, 0);      // Green
        original.SetPixel(2, 0, 0, 0, 255);      // Blue
        original.SetPixel(3, 0, 255, 255, 255);  // White
        original.SetPixel(0, 1, 0, 0, 0);        // Black
        original.SetPixel(1, 1, 128, 128, 128);  // Gray
        original.SetPixel(2, 1, 255, 0, 0, 128);      // Semi-transparent red

        byte[] encoded = PngEncoder.Encode(original);
        PngImage decoded = PngDecoder.Decode(encoded);

        Assert.Equal(original.Width, decoded.Width);
        Assert.Equal(original.Height, decoded.Height);

        Assert.Equal((255, 0, 0, 255), decoded.GetPixel(0, 0));
        Assert.Equal((0, 255, 0, 255), decoded.GetPixel(1, 0));
        Assert.Equal((0, 0, 255, 255), decoded.GetPixel(2, 0));
        Assert.Equal((255, 255, 255, 255), decoded.GetPixel(3, 0));
        Assert.Equal((0, 0, 0, 255), decoded.GetPixel(0, 1));
        Assert.Equal((128, 128, 128, 255), decoded.GetPixel(1, 1));
        Assert.Equal((255, 0, 0, 128), decoded.GetPixel(2, 1));
    }

    [Fact]
    public void RoundTrip_Rgb_PreservesPixels()
    {
        var original = new PngImage(4, 4);
        original.SetPixel(0, 0, 255, 0, 0);      // Red
        original.SetPixel(1, 0, 0, 255, 0);      // Green
        original.SetPixel(2, 0, 0, 0, 255);      // Blue
        original.SetPixel(3, 0, 255, 255, 255);  // White

        byte[] encoded = PngEncoder.Encode(original, PngColorType.Rgb);
        PngImage decoded = PngDecoder.Decode(encoded);

        Assert.Equal(original.Width, decoded.Width);
        Assert.Equal(original.Height, decoded.Height);

        Assert.Equal((255, 0, 0, 255), decoded.GetPixel(0, 0));
        Assert.Equal((0, 255, 0, 255), decoded.GetPixel(1, 0));
        Assert.Equal((0, 0, 255, 255), decoded.GetPixel(2, 0));
        Assert.Equal((255, 255, 255, 255), decoded.GetPixel(3, 0));
    }

    [Fact]
    public void RoundTrip_Grayscale_PreservesPixels()
    {
        var original = new PngImage(4, 4);
        original.SetPixel(0, 0, 0, 0, 0);        // Black
        original.SetPixel(1, 0, 64, 64, 64);     // Dark gray
        original.SetPixel(2, 0, 128, 128, 128);  // Mid gray
        original.SetPixel(3, 0, 255, 255, 255);  // White

        byte[] encoded = PngEncoder.Encode(original, PngColorType.Grayscale);
        PngImage decoded = PngDecoder.Decode(encoded);

        Assert.Equal(original.Width, decoded.Width);
        Assert.Equal(original.Height, decoded.Height);

        Assert.Equal((0, 0, 0, 255), decoded.GetPixel(0, 0));
        Assert.Equal((64, 64, 64, 255), decoded.GetPixel(1, 0));
        Assert.Equal((128, 128, 128, 255), decoded.GetPixel(2, 0));
        Assert.Equal((255, 255, 255, 255), decoded.GetPixel(3, 0));
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
        var original = new PngImage(width, height);

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

        byte[] encoded = PngEncoder.Encode(original);
        PngImage decoded = PngDecoder.Decode(encoded);

        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);

        // Verify all pixels
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

    [Fact]
    public void RoundTrip_AllFilters_Work()
    {
        // Create an image that will exercise different filter types
        var original = new PngImage(16, 16);

        // Horizontal gradient (good for Sub filter)
        for (var x = 0; x < 16; x++)
        {
            original.SetPixel(x, 0, (byte)(x * 16), 0, 0);
        }

        // Vertical gradient (good for Up filter)
        for (var y = 0; y < 16; y++)
        {
            original.SetPixel(0, y, 0, (byte)(y * 16), 0);
        }

        // Diagonal (good for Average/Paeth)
        for (var i = 0; i < 16; i++)
        {
            original.SetPixel(i, i, (byte)(i * 16), (byte)(i * 16), (byte)(i * 16));
        }

        byte[] encoded = PngEncoder.Encode(original);
        PngImage decoded = PngDecoder.Decode(encoded);

        // Verify specific pixels
        Assert.Equal((0, 0, 0, 255), decoded.GetPixel(0, 0));
        Assert.Equal((240, 0, 0, 255), decoded.GetPixel(15, 0));
        Assert.Equal((0, 240, 0, 255), decoded.GetPixel(0, 15));
    }
}

public class PngHeaderTests
{
    [Fact]
    public void Decode_TooSmall_Throws()
    {
        var data = new byte[10];
        Assert.Throws<PngException>(() => PngDecoder.Decode(data));
    }

    [Fact]
    public void Decode_InvalidSignature_Throws()
    {
        var data = new byte[50];
        data[0] = (byte)'G';
        data[1] = (byte)'I';
        data[2] = (byte)'F';

        Assert.Throws<PngException>(() => PngDecoder.Decode(data));
    }

    [Fact]
    public void Encode_ValidPngSignature()
    {
        var image = new PngImage(1, 1);
        image.SetPixel(0, 0, 128, 128, 128);
        byte[] data = PngEncoder.Encode(image);

        // Check PNG signature
        Assert.Equal(137, data[0]);
        Assert.Equal(80, data[1]);   // P
        Assert.Equal(78, data[2]);   // N
        Assert.Equal(71, data[3]);   // G
        Assert.Equal(13, data[4]);
        Assert.Equal(10, data[5]);
        Assert.Equal(26, data[6]);
        Assert.Equal(10, data[7]);
    }

    [Fact]
    public void Encode_HasIhdrChunk()
    {
        var image = new PngImage(10, 20);
        byte[] data = PngEncoder.Encode(image);

        // First chunk after signature should be IHDR
        // Signature (8) + Length (4) + Type (4)
        string chunkType = Encoding.ASCII.GetString(data, 12, 4);
        Assert.Equal("IHDR", chunkType);
    }

    [Fact]
    public void Encode_HasIendChunk()
    {
        var image = new PngImage(1, 1);
        image.SetPixel(0, 0, 128, 128, 128);
        byte[] data = PngEncoder.Encode(image);

        // IEND should be at the end: Length(4) + "IEND"(4) + CRC(4) = 12 bytes from end
        string chunkType = Encoding.ASCII.GetString(data, data.Length - 12 + 4, 4);
        Assert.Equal("IEND", chunkType);
    }
}

public class PngImageTests
{
    [Fact]
    public void Constructor_InvalidWidth_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PngImage(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PngImage(-1, 10));
    }

    [Fact]
    public void Constructor_InvalidHeight_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PngImage(10, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PngImage(10, -1));
    }

    [Fact]
    public void GetPixel_OutOfRange_Throws()
    {
        var image = new PngImage(10, 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => image.GetPixel(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.GetPixel(10, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.GetPixel(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.GetPixel(0, 10));
    }

    [Fact]
    public void SetPixel_OutOfRange_Throws()
    {
        var image = new PngImage(10, 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => image.SetPixel(-1, 0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.SetPixel(10, 0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.SetPixel(0, -1, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.SetPixel(0, 10, 0, 0, 0));
    }

    [Fact]
    public void SetPixel_GetPixel_RoundTrip()
    {
        var image = new PngImage(10, 10);

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
        var image = new PngImage(5, 5);

        (byte R, byte G, byte B, byte A) pixel = image.GetPixel(2, 2);
        Assert.Equal(0, pixel.R);
        Assert.Equal(0, pixel.G);
        Assert.Equal(0, pixel.B);
        Assert.Equal(0, pixel.A);
    }
}
