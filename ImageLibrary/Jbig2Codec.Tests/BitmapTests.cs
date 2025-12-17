using ImageLibrary.Jbig2;
using Xunit;

namespace Jbig2Codec.Tests;

public class BitmapTests
{
    [Fact]
    public void Constructor_SetsCorrectDimensions()
    {
        var bitmap = new Bitmap(100, 50);

        Assert.Equal(100, bitmap.Width);
        Assert.Equal(50, bitmap.Height);
        Assert.Equal(13, bitmap.Stride); // (100 + 7) / 8 = 13
    }

    [Fact]
    public void Constructor_CalculatesStrideCorrectly()
    {
        // Width 8 = stride 1
        Assert.Equal(1, new Bitmap(8, 1).Stride);
        // Width 9 = stride 2
        Assert.Equal(2, new Bitmap(9, 1).Stride);
        // Width 16 = stride 2
        Assert.Equal(2, new Bitmap(16, 1).Stride);
        // Width 17 = stride 3
        Assert.Equal(3, new Bitmap(17, 1).Stride);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(10, 0)]
    [InlineData(10, -1)]
    public void Constructor_ThrowsOnInvalidDimensions(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bitmap(width, height));
    }

    [Fact]
    public void NewBitmap_IsAllZeros()
    {
        var bitmap = new Bitmap(16, 16);

        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                Assert.Equal(0, bitmap.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void SetPixel_GetPixel_RoundTrips()
    {
        var bitmap = new Bitmap(16, 16);

        bitmap.SetPixel(5, 7, 1);
        Assert.Equal(1, bitmap.GetPixel(5, 7));

        bitmap.SetPixel(5, 7, 0);
        Assert.Equal(0, bitmap.GetPixel(5, 7));
    }

    [Fact]
    public void SetPixel_SetsCorrectBit()
    {
        var bitmap = new Bitmap(16, 1);

        // Set pixel 0 (MSB of first byte)
        bitmap.SetPixel(0, 0, 1);
        Assert.Equal(0x80, bitmap.Data[0]);

        // Set pixel 7 (LSB of first byte)
        bitmap.SetPixel(7, 0, 1);
        Assert.Equal(0x81, bitmap.Data[0]);

        // Set pixel 8 (MSB of second byte)
        bitmap.SetPixel(8, 0, 1);
        Assert.Equal(0x80, bitmap.Data[1]);
    }

    [Fact]
    public void GetPixel_OutOfBounds_ReturnsZero()
    {
        var bitmap = new Bitmap(10, 10);
        bitmap.Fill(1); // Fill with 1s

        Assert.Equal(0, bitmap.GetPixel(-1, 0));
        Assert.Equal(0, bitmap.GetPixel(0, -1));
        Assert.Equal(0, bitmap.GetPixel(10, 0));
        Assert.Equal(0, bitmap.GetPixel(0, 10));
    }

    [Fact]
    public void SetPixel_OutOfBounds_IsIgnored()
    {
        var bitmap = new Bitmap(10, 10);

        // Should not throw
        bitmap.SetPixel(-1, 0, 1);
        bitmap.SetPixel(0, -1, 1);
        bitmap.SetPixel(10, 0, 1);
        bitmap.SetPixel(0, 10, 1);

        // All pixels should still be 0
        for (var y = 0; y < 10; y++)
        {
            for (var x = 0; x < 10; x++)
            {
                Assert.Equal(0, bitmap.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void Fill_SetsAllPixels()
    {
        var bitmap = new Bitmap(16, 16);

        bitmap.Fill(1);
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                Assert.Equal(1, bitmap.GetPixel(x, y));
            }
        }

        bitmap.Fill(0);
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                Assert.Equal(0, bitmap.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void Clone_CreatesIdenticalCopy()
    {
        var original = new Bitmap(16, 16);
        original.SetPixel(5, 5, 1);
        original.SetPixel(10, 10, 1);

        Bitmap clone = original.Clone();

        Assert.Equal(original.Width, clone.Width);
        Assert.Equal(original.Height, clone.Height);
        Assert.Equal(1, clone.GetPixel(5, 5));
        Assert.Equal(1, clone.GetPixel(10, 10));
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        var original = new Bitmap(16, 16);
        original.SetPixel(5, 5, 1);

        Bitmap clone = original.Clone();
        clone.SetPixel(5, 5, 0);
        clone.SetPixel(7, 7, 1);

        // Original should be unchanged
        Assert.Equal(1, original.GetPixel(5, 5));
        Assert.Equal(0, original.GetPixel(7, 7));
    }

    [Fact]
    public void Blit_Or_CombinesCorrectly()
    {
        var dest = new Bitmap(8, 8);
        var src = new Bitmap(4, 4);
        src.Fill(1);

        dest.Blit(src, 2, 2);

        // Check the 4x4 region at (2,2) is set
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                int expected = (x >= 2 && x < 6 && y >= 2 && y < 6) ? 1 : 0;
                Assert.Equal(expected, dest.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void Blit_And_CombinesCorrectly()
    {
        var dest = new Bitmap(8, 8);
        dest.Fill(1);

        var src = new Bitmap(4, 4);
        src.Fill(0);

        dest.Blit(src, 2, 2, CombinationOperator.And);

        // Check the 4x4 region at (2,2) is cleared, rest is still 1
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                int expected = (x >= 2 && x < 6 && y >= 2 && y < 6) ? 0 : 1;
                Assert.Equal(expected, dest.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void Blit_Xor_CombinesCorrectly()
    {
        var dest = new Bitmap(8, 1);
        dest.SetPixel(0, 0, 1);
        dest.SetPixel(1, 0, 1);
        dest.SetPixel(2, 0, 0);
        dest.SetPixel(3, 0, 0);

        var src = new Bitmap(4, 1);
        src.SetPixel(0, 0, 1);
        src.SetPixel(1, 0, 0);
        src.SetPixel(2, 0, 1);
        src.SetPixel(3, 0, 0);

        dest.Blit(src, 0, 0, CombinationOperator.Xor);

        Assert.Equal(0, dest.GetPixel(0, 0)); // 1 ^ 1 = 0
        Assert.Equal(1, dest.GetPixel(1, 0)); // 1 ^ 0 = 1
        Assert.Equal(1, dest.GetPixel(2, 0)); // 0 ^ 1 = 1
        Assert.Equal(0, dest.GetPixel(3, 0)); // 0 ^ 0 = 0
    }

    [Fact]
    public void Blit_Replace_OverwritesDestination()
    {
        var dest = new Bitmap(8, 8);
        dest.Fill(1);

        var src = new Bitmap(4, 4);
        // src is all zeros

        dest.Blit(src, 2, 2, CombinationOperator.Replace);

        // The 4x4 region at (2,2) should be 0, rest should be 1
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                int expected = (x >= 2 && x < 6 && y >= 2 && y < 6) ? 0 : 1;
                Assert.Equal(expected, dest.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void Blit_NegativeOffset_ClipsCorrectly()
    {
        var dest = new Bitmap(8, 8);
        var src = new Bitmap(4, 4);
        src.Fill(1);

        dest.Blit(src, -2, -2);

        // Only 2x2 region at (0,0) should be set
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                int expected = (x < 2 && y < 2) ? 1 : 0;
                Assert.Equal(expected, dest.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void Blit_PartiallyOutOfBounds_ClipsCorrectly()
    {
        var dest = new Bitmap(8, 8);
        var src = new Bitmap(4, 4);
        src.Fill(1);

        dest.Blit(src, 6, 6);

        // Only 2x2 region at (6,6) should be set
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                int expected = (x >= 6 && y >= 6) ? 1 : 0;
                Assert.Equal(expected, dest.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void Constructor_WithData_UsesProvidedData()
    {
        var data = new byte[] { 0xAA, 0x55 }; // 10101010 01010101
        var bitmap = new Bitmap(16, 1, data);

        Assert.Equal(1, bitmap.GetPixel(0, 0));
        Assert.Equal(0, bitmap.GetPixel(1, 0));
        Assert.Equal(1, bitmap.GetPixel(2, 0));
        Assert.Equal(0, bitmap.GetPixel(3, 0));
    }

    [Fact]
    public void Constructor_WithData_ThrowsIfTooSmall()
    {
        var data = new byte[] { 0x00 }; // Only 1 byte

        Assert.Throws<ArgumentException>(() => new Bitmap(16, 1, data)); // Needs 2 bytes
    }

    [Fact]
    public void Data_ReturnsInternalBuffer()
    {
        var bitmap = new Bitmap(8, 2);
        bitmap.SetPixel(0, 0, 1);

        Assert.Equal(0x80, bitmap.Data[0]);
        Assert.Equal(2, bitmap.Data.Length);
    }
}
