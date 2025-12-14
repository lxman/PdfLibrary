namespace Compressors.Jpeg.Tests;

public class JpegTests
{
    #region DCT Tests

    [Fact]
    public void Dct_ForwardInverse_ReturnsOriginal()
    {
        // Create a test block with known values
        var original = new float[64];
        for (var i = 0; i < 64; i++)
        {
            original[i] = (i * 4) - 128; // Values from -128 to 124
        }

        var block = new float[64];
        original.CopyTo(block, 0);

        // Forward DCT
        Dct.ForwardDct(block);

        // Inverse DCT
        Dct.InverseDct(block);

        // Check that we get back approximately the original values
        for (var i = 0; i < 64; i++)
        {
            Assert.True(Math.Abs(block[i] - original[i]) < 0.5f,
                $"Mismatch at index {i}: expected {original[i]}, got {block[i]}");
        }
    }

    [Fact]
    public void Dct_AllZeros_ReturnsAllZeros()
    {
        var block = new float[64];

        Dct.ForwardDct(block);

        for (var i = 0; i < 64; i++)
        {
            Assert.True(Math.Abs(block[i]) < 0.001f, $"Expected zero at index {i}, got {block[i]}");
        }
    }

    [Fact]
    public void Dct_ConstantBlock_HasOnlyDcCoefficient()
    {
        var block = new float[64];
        for (var i = 0; i < 64; i++)
        {
            block[i] = 100;
        }

        Dct.ForwardDct(block);

        // DC coefficient should be non-zero
        Assert.True(Math.Abs(block[0]) > 1, "DC coefficient should be non-zero");

        // All other coefficients should be approximately zero
        for (var i = 1; i < 64; i++)
        {
            Assert.True(Math.Abs(block[i]) < 0.001f,
                $"AC coefficient at index {i} should be zero, got {block[i]}");
        }
    }

    [Fact]
    public void Dct_ReferenceMatchesFast()
    {
        var block1 = new float[64];
        var block2 = new float[64];
        var random = new Random(42);

        for (var i = 0; i < 64; i++)
        {
            float value = random.Next(-128, 128);
            block1[i] = value;
            block2[i] = value;
        }

        Dct.ForwardDct(block1);
        Dct.ForwardDctReference(block2);

        // Check that both implementations produce similar results
        for (var i = 0; i < 64; i++)
        {
            Assert.True(Math.Abs(block1[i] - block2[i]) < 1.0f,
                $"Mismatch at index {i}: fast={block1[i]}, reference={block2[i]}");
        }
    }

    #endregion

    #region Quantization Tests

    [Fact]
    public void Quantization_GenerateTable_Quality50_ReturnsStandardTable()
    {
        int[] table = Quantization.GenerateLuminanceQuantTable(50);

        // At quality 50, the scale factor is 100, so values should be approximately the standard table
        Assert.Equal(64, table.Length);
        Assert.Equal(16, table[0]); // First element of standard luminance table
    }

    [Fact]
    public void Quantization_GenerateTable_Quality1_MaximumQuantization()
    {
        int[] table = Quantization.GenerateLuminanceQuantTable(1);

        // At quality 1, the scale factor is 5000, so values should be much larger
        Assert.True(table[0] >= 255 || table[0] == 255); // Clamped to 255
    }

    [Fact]
    public void Quantization_GenerateTable_Quality100_MinimumQuantization()
    {
        int[] table = Quantization.GenerateLuminanceQuantTable(100);

        // At quality 100, the scale factor is 0, so all values should be 1
        for (var i = 0; i < 64; i++)
        {
            Assert.Equal(1, table[i]);
        }
    }

    [Fact]
    public void Quantization_ZigzagRoundtrip()
    {
        var natural = new int[64];
        for (var i = 0; i < 64; i++)
        {
            natural[i] = i;
        }

        var zigzag = new int[64];
        var restored = new int[64];

        Quantization.ToZigzag(natural, zigzag);
        Quantization.FromZigzag(zigzag, restored);

        for (var i = 0; i < 64; i++)
        {
            Assert.Equal(natural[i], restored[i]);
        }
    }

    #endregion

    #region Color Conversion Tests

    [Fact]
    public void ColorConversion_RgbToYCbCr_Black()
    {
        ColorConversion.RgbToYCbCr(0, 0, 0, out byte y, out byte cb, out byte cr);

        Assert.Equal(0, y);
        Assert.Equal(128, cb);
        Assert.Equal(128, cr);
    }

    [Fact]
    public void ColorConversion_RgbToYCbCr_White()
    {
        ColorConversion.RgbToYCbCr(255, 255, 255, out byte y, out byte cb, out byte cr);

        Assert.Equal(255, y);
        Assert.Equal(128, cb);
        Assert.Equal(128, cr);
    }

    [Fact]
    public void ColorConversion_Roundtrip()
    {
        // Test various colors
        var testColors = new (byte R, byte G, byte B)[]
        {
            (255, 0, 0),   // Red
            (0, 255, 0),   // Green
            (0, 0, 255),   // Blue
            (128, 128, 128), // Gray
            (200, 100, 50),  // Random
        };

        foreach ((byte r, byte g, byte b) in testColors)
        {
            ColorConversion.RgbToYCbCr(r, g, b, out byte y, out byte cb, out byte cr);
            ColorConversion.YCbCrToRgb(y, cb, cr, out byte r2, out byte g2, out byte b2);

            // Allow small error due to rounding
            Assert.True(Math.Abs(r - r2) <= 2, $"Red mismatch: {r} -> {r2}");
            Assert.True(Math.Abs(g - g2) <= 2, $"Green mismatch: {g} -> {g2}");
            Assert.True(Math.Abs(b - b2) <= 2, $"Blue mismatch: {b} -> {b2}");
        }
    }

    #endregion

    #region Huffman Table Tests

    [Fact]
    public void HuffmanTable_StandardTables_CreateSuccessfully()
    {
        var dcLum = HuffmanTable.CreateDcLuminance();
        var dcChr = HuffmanTable.CreateDcChrominance();
        var acLum = HuffmanTable.CreateAcLuminance();
        var acChr = HuffmanTable.CreateAcChrominance();

        Assert.NotNull(dcLum);
        Assert.NotNull(dcChr);
        Assert.NotNull(acLum);
        Assert.NotNull(acChr);
    }

    [Fact]
    public void HuffmanTable_Encode_ValidSymbols()
    {
        var dcTable = HuffmanTable.CreateDcLuminance();

        // DC luminance table has symbols 0-11
        for (byte i = 0; i <= 11; i++)
        {
            (ushort code, byte length) = dcTable.Encode(i);
            Assert.True(length > 0, $"Symbol {i} should have positive length");
            Assert.True(length <= 16, $"Symbol {i} should have length <= 16");
        }
    }

    #endregion

    #region BitReader/BitWriter Tests

    [Fact]
    public void BitWriter_BitReader_Roundtrip()
    {
        using var stream = new MemoryStream();

        using (var writer = new BitWriter(stream))
        {
            writer.WriteBits(0b10110, 5);
            writer.WriteBits(0b11, 2);
            writer.WriteBits(0b10101010, 8);
        }

        stream.Position = 0;
        var reader = new BitReader(stream);

        Assert.Equal(0b10110, reader.ReadBits(5));
        Assert.Equal(0b11, reader.ReadBits(2));
        Assert.Equal(0b10101010, reader.ReadBits(8));
    }

    [Fact]
    public void BitWriter_ByteStuffing()
    {
        using var stream = new MemoryStream();

        using (var writer = new BitWriter(stream))
        {
            // Write 0xFF byte
            writer.WriteBits(0xFF, 8);
        }

        byte[] bytes = stream.ToArray();

        // Should have 0xFF followed by 0x00 (stuffing) plus padding
        Assert.True(bytes.Length >= 2);
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0x00, bytes[1]);
    }

    [Fact]
    public void BitReader_Extend_PositiveValue()
    {
        // For 3-bit values, threshold is 4
        // Values 4-7 are positive (4, 5, 6, 7)
        Assert.Equal(4, BitReader.Extend(4, 3));
        Assert.Equal(5, BitReader.Extend(5, 3));
        Assert.Equal(7, BitReader.Extend(7, 3));
    }

    [Fact]
    public void BitReader_Extend_NegativeValue()
    {
        // For 3-bit values, threshold is 4
        // Values 0-3 are negative (-7, -6, -5, -4)
        Assert.Equal(-7, BitReader.Extend(0, 3));
        Assert.Equal(-6, BitReader.Extend(1, 3));
        Assert.Equal(-5, BitReader.Extend(2, 3));
        Assert.Equal(-4, BitReader.Extend(3, 3));
    }

    #endregion

    #region Encode/Decode Tests

    [Fact]
    public void Jpeg_EncodeDecodeGrayscale_Roundtrip()
    {
        // Create a simple 8x8 grayscale image
        var gray = new byte[64];
        for (var i = 0; i < 64; i++)
        {
            gray[i] = (byte)(i * 4);
        }

        // Encode
        byte[] jpegData = Jpeg.EncodeGrayscale(gray, 8, 8, 95);

        // Verify it's a valid JPEG
        Assert.Equal(0xFF, jpegData[0]);
        Assert.Equal(JpegConstants.SOI, jpegData[1]);

        // Get info
        JpegInfo info = Jpeg.GetInfo(jpegData);
        Assert.Equal(8, info.Width);
        Assert.Equal(8, info.Height);
        Assert.True(info.IsGrayscale);

        // Decode
        byte[] decoded = Jpeg.Decode(jpegData, out int width, out int height);
        Assert.Equal(8, width);
        Assert.Equal(8, height);

        // Verify we get approximately the same values (lossy compression)
        for (var i = 0; i < 64; i++)
        {
            // RGB output for grayscale has R=G=B
            byte decodedGray = decoded[i * 3];
            int diff = Math.Abs(gray[i] - decodedGray);
            Assert.True(diff < 10, $"Pixel {i}: original={gray[i]}, decoded={decodedGray}, diff={diff}");
        }
    }

    [Fact]
    public void Jpeg_EncodeDecodeColor_Roundtrip()
    {
        // Create a simple 8x8 color image
        var rgb = new byte[64 * 3];
        for (var i = 0; i < 64; i++)
        {
            rgb[i * 3] = (byte)(i * 4);     // R
            rgb[i * 3 + 1] = (byte)(128);   // G
            rgb[i * 3 + 2] = (byte)(255 - i * 4); // B
        }

        // Encode with high quality to minimize loss
        byte[] jpegData = Jpeg.Encode(rgb, 8, 8, 95, JpegSubsampling.Subsampling444);

        // Verify it's a valid JPEG
        Assert.Equal(0xFF, jpegData[0]);
        Assert.Equal(JpegConstants.SOI, jpegData[1]);

        // Get info
        JpegInfo info = Jpeg.GetInfo(jpegData);
        Assert.Equal(8, info.Width);
        Assert.Equal(8, info.Height);
        Assert.True(info.IsColor);

        // Decode
        byte[] decoded = Jpeg.Decode(jpegData, out int width, out int height);
        Assert.Equal(8, width);
        Assert.Equal(8, height);
        Assert.Equal(64 * 3, decoded.Length);
    }

    [Fact]
    public void Jpeg_EncodeDecodeColor_LargerImage()
    {
        // Create a 32x32 color image with gradient
        var width = 32;
        var height = 32;
        var rgb = new byte[width * height * 3];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int idx = (y * width + x) * 3;
                rgb[idx] = (byte)(x * 8);      // R: horizontal gradient
                rgb[idx + 1] = (byte)(y * 8);  // G: vertical gradient
                rgb[idx + 2] = 128;            // B: constant
            }
        }

        // Encode
        byte[] jpegData = Jpeg.Encode(rgb, width, height, 85);

        // Decode
        byte[] decoded = Jpeg.Decode(jpegData, out int decWidth, out int decHeight);

        Assert.Equal(width, decWidth);
        Assert.Equal(height, decHeight);
        Assert.Equal(width * height * 3, decoded.Length);

        // Verify PSNR is reasonable (lossy compression)
        double mse = rgb.Select((t, i) => t - decoded[i]).Sum(diff => diff * (double)diff);
        mse /= rgb.Length;
        double psnr = 10 * Math.Log10(255 * 255 / mse);

        Assert.True(psnr > 25, $"PSNR {psnr:F2} dB is too low for quality 85");
    }

    [Fact]
    public void Jpeg_GetInfo_ValidJpeg()
    {
        // Create a minimal JPEG
        var rgb = new byte[8 * 8 * 3];
        byte[] jpegData = Jpeg.Encode(rgb, 8, 8, 75);

        JpegInfo info = Jpeg.GetInfo(jpegData);

        Assert.Equal(8, info.Width);
        Assert.Equal(8, info.Height);
        Assert.Equal(8, info.BitsPerSample);
        Assert.Equal(3, info.ComponentCount);
        Assert.True(info.IsBaseline);
        Assert.False(info.IsProgressive);
    }

    #endregion

    #region Quality Tests

    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(75)]
    [InlineData(90)]
    [InlineData(100)]
    public void Jpeg_DifferentQualities_ProduceValidJpeg(int quality)
    {
        var rgb = new byte[16 * 16 * 3];
        var random = new Random(42);
        random.NextBytes(rgb);

        byte[] jpegData = Jpeg.Encode(rgb, 16, 16, quality);

        // Should be able to decode without error
        byte[] decoded = Jpeg.Decode(jpegData, out int width, out int height);

        Assert.Equal(16, width);
        Assert.Equal(16, height);
    }

    [Theory]
    [InlineData(JpegSubsampling.Subsampling444)]
    [InlineData(JpegSubsampling.Subsampling422)]
    [InlineData(JpegSubsampling.Subsampling420)]
    public void Jpeg_DifferentSubsampling_ProduceValidJpeg(JpegSubsampling subsampling)
    {
        var rgb = new byte[16 * 16 * 3];
        var random = new Random(42);
        random.NextBytes(rgb);

        byte[] jpegData = Jpeg.Encode(rgb, 16, 16, 75, subsampling);

        byte[] decoded = Jpeg.Decode(jpegData, out int width, out int height);

        Assert.Equal(16, width);
        Assert.Equal(16, height);
    }

    #endregion
}
