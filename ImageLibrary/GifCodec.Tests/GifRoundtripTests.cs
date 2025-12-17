using ImageLibrary.Gif;
using Xunit;

namespace GifCodec.Tests;

public class GifRoundtripTests
{
    [Fact]
    public void RoundTrip_SolidColor_PreservesPixels()
    {
        var original = new GifImage(4, 4);
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                original.SetPixel(x, y, 255, 0, 0);
            }
        }

        byte[] encoded = GifEncoder.Encode(original);
        GifImage decoded = GifDecoder.DecodeFirstFrame(encoded);

        Assert.Equal(original.Width, decoded.Width);
        Assert.Equal(original.Height, decoded.Height);

        (byte R, byte G, byte B, byte A) pixel = decoded.GetPixel(0, 0);
        Assert.Equal(255, pixel.R);
        Assert.Equal(0, pixel.G);
        Assert.Equal(0, pixel.B);
        Assert.Equal(255, pixel.A);
    }

    [Fact]
    public void RoundTrip_MultipleColors_PreservesPixels()
    {
        var original = new GifImage(4, 4);
        original.SetPixel(0, 0, 255, 0, 0);      // Red
        original.SetPixel(1, 0, 0, 255, 0);      // Green
        original.SetPixel(2, 0, 0, 0, 255);      // Blue
        original.SetPixel(3, 0, 255, 255, 255);  // White
        original.SetPixel(0, 1, 0, 0, 0);        // Black
        original.SetPixel(1, 1, 128, 128, 128);  // Gray

        byte[] encoded = GifEncoder.Encode(original);
        GifImage decoded = GifDecoder.DecodeFirstFrame(encoded);

        Assert.Equal(original.Width, decoded.Width);
        Assert.Equal(original.Height, decoded.Height);

        // Check specific colors
        Assert.Equal((255, 0, 0, 255), decoded.GetPixel(0, 0));
        Assert.Equal((0, 255, 0, 255), decoded.GetPixel(1, 0));
        Assert.Equal((0, 0, 255, 255), decoded.GetPixel(2, 0));
        Assert.Equal((255, 255, 255, 255), decoded.GetPixel(3, 0));
        Assert.Equal((0, 0, 0, 255), decoded.GetPixel(0, 1));
        Assert.Equal((128, 128, 128, 255), decoded.GetPixel(1, 1));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(7, 3)]
    [InlineData(50, 50)]
    public void RoundTrip_VariousSizes_Works(int width, int height)
    {
        var original = new GifImage(width, height);

        // Fill with a simple pattern
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var r = (byte)(x * 255 / Math.Max(1, width - 1));
                var g = (byte)(y * 255 / Math.Max(1, height - 1));
                original.SetPixel(x, y, r, g, 128);
            }
        }

        byte[] encoded = GifEncoder.Encode(original);
        GifImage decoded = GifDecoder.DecodeFirstFrame(encoded);

        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);
    }

    [Fact]
    public void RoundTrip_Gradient_ApproximatesColors()
    {
        // GIF only supports 256 colors, so gradient will be quantized
        var original = new GifImage(16, 16);

        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                var r = (byte)(x * 17);  // 0-255
                var g = (byte)(y * 17);
                original.SetPixel(x, y, r, g, 128);
            }
        }

        byte[] encoded = GifEncoder.Encode(original);
        GifImage decoded = GifDecoder.DecodeFirstFrame(encoded);

        Assert.Equal(16, decoded.Width);
        Assert.Equal(16, decoded.Height);

        // Due to color quantization, pixels won't be exact but should be close
        // Check corners
        (byte R, byte G, byte B, byte A) topLeft = decoded.GetPixel(0, 0);
        Assert.True(topLeft.R < 30);  // Should be close to 0
        Assert.True(topLeft.G < 30);

        (byte R, byte G, byte B, byte A) bottomRight = decoded.GetPixel(15, 15);
        Assert.True(bottomRight.R > 200);  // Should be close to 255
        Assert.True(bottomRight.G > 200);
    }
}

public class GifHeaderTests
{
    [Fact]
    public void Decode_TooSmall_Throws()
    {
        var data = new byte[10];
        Assert.Throws<GifException>(() => GifDecoder.Decode(data));
    }

    [Fact]
    public void Decode_InvalidSignature_Throws()
    {
        var data = new byte[20];
        data[0] = (byte)'P';
        data[1] = (byte)'N';
        data[2] = (byte)'G';

        Assert.Throws<GifException>(() => GifDecoder.Decode(data));
    }

    [Fact]
    public void Encode_ValidGifSignature()
    {
        var image = new GifImage(1, 1);
        image.SetPixel(0, 0, 128, 128, 128);
        byte[] data = GifEncoder.Encode(image);

        // Check GIF89a signature
        Assert.Equal((byte)'G', data[0]);
        Assert.Equal((byte)'I', data[1]);
        Assert.Equal((byte)'F', data[2]);
        Assert.Equal((byte)'8', data[3]);
        Assert.Equal((byte)'9', data[4]);
        Assert.Equal((byte)'a', data[5]);
    }

    [Fact]
    public void Encode_ValidTrailer()
    {
        var image = new GifImage(1, 1);
        image.SetPixel(0, 0, 128, 128, 128);
        byte[] data = GifEncoder.Encode(image);

        // Last byte should be trailer (0x3B)
        Assert.Equal(0x3B, data[data.Length - 1]);
    }

    [Fact]
    public void Encode_InvalidMaxColors_Throws()
    {
        var image = new GifImage(1, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => GifEncoder.Encode(image, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GifEncoder.Encode(image, 257));
    }
}

public class GifImageTests
{
    [Fact]
    public void Constructor_InvalidWidth_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GifImage(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GifImage(-1, 10));
    }

    [Fact]
    public void Constructor_InvalidHeight_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GifImage(10, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GifImage(10, -1));
    }

    [Fact]
    public void GetPixel_OutOfRange_Throws()
    {
        var image = new GifImage(10, 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => image.GetPixel(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.GetPixel(10, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.GetPixel(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.GetPixel(0, 10));
    }

    [Fact]
    public void SetPixel_OutOfRange_Throws()
    {
        var image = new GifImage(10, 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => image.SetPixel(-1, 0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.SetPixel(10, 0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.SetPixel(0, -1, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.SetPixel(0, 10, 0, 0, 0));
    }

    [Fact]
    public void SetPixel_GetPixel_RoundTrip()
    {
        var image = new GifImage(10, 10);

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
        var image = new GifImage(5, 5);

        (byte R, byte G, byte B, byte A) pixel = image.GetPixel(2, 2);
        Assert.Equal(0, pixel.R);
        Assert.Equal(0, pixel.G);
        Assert.Equal(0, pixel.B);
        Assert.Equal(0, pixel.A);
    }
}

public class GifFileTests
{
    [Fact]
    public void Constructor_InvalidWidth_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GifFile(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GifFile(-1, 10));
    }

    [Fact]
    public void Constructor_InvalidHeight_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GifFile(10, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GifFile(10, -1));
    }

    [Fact]
    public void FirstFrame_Empty_ReturnsNull()
    {
        var gifFile = new GifFile(10, 10);
        Assert.Null(gifFile.FirstFrame);
    }

    [Fact]
    public void FirstFrame_WithFrame_ReturnsFirst()
    {
        var gifFile = new GifFile(10, 10);
        var frame = new GifImage(10, 10);
        gifFile.Frames.Add(frame);

        Assert.Same(frame, gifFile.FirstFrame);
    }

    [Fact]
    public void Encode_NoFrames_Throws()
    {
        var gifFile = new GifFile(10, 10);
        Assert.Throws<GifException>(() => GifEncoder.Encode(gifFile));
    }
}

public class LzwTests
{
    [Fact]
    public void RoundTrip_SmallData()
    {
        // Create a simple GIF and verify LZW round-trips correctly
        var image = new GifImage(2, 2);
        image.SetPixel(0, 0, 255, 0, 0);
        image.SetPixel(1, 0, 0, 255, 0);
        image.SetPixel(0, 1, 0, 0, 255);
        image.SetPixel(1, 1, 255, 255, 0);

        byte[] encoded = GifEncoder.Encode(image);
        GifImage decoded = GifDecoder.DecodeFirstFrame(encoded);

        Assert.Equal(2, decoded.Width);
        Assert.Equal(2, decoded.Height);
    }

    [Fact]
    public void RoundTrip_RepetitiveData()
    {
        // Repetitive data should compress well
        var image = new GifImage(100, 100);
        for (var y = 0; y < 100; y++)
        {
            for (var x = 0; x < 100; x++)
            {
                image.SetPixel(x, y, 128, 64, 32);
            }
        }

        byte[] encoded = GifEncoder.Encode(image);
        GifImage decoded = GifDecoder.DecodeFirstFrame(encoded);

        Assert.Equal(100, decoded.Width);
        Assert.Equal(100, decoded.Height);
        Assert.Equal((128, 64, 32, 255), decoded.GetPixel(50, 50));

        // Verify compression actually happened
        int uncompressedSize = 100 * 100 + 800; // Rough estimate
        Assert.True(encoded.Length < uncompressedSize);
    }
}
