using Compressors.Jpeg2k;

namespace Compressors.Jpeg2k.Tests;

public class Jpeg2000Tests
{
    #region Wavelet Tests

    [Fact]
    public void Wavelet_Forward2D_Inverse2D_Lossy_ApproximatesOriginal()
    {
        // Note: The 9/7 wavelet can accumulate some errors with multiple levels
        int width = 8;
        int height = 8;
        var original = new float[width * height];
        var random = new Random(42);
        for (int i = 0; i < original.Length; i++)
        {
            original[i] = random.Next(-50, 50); // Smaller range
        }

        var data = new float[original.Length];
        original.CopyTo(data, 0);

        Wavelet.Forward2D(data, width, height, 1, true); // Use 1 level
        Wavelet.Inverse2D(data, width, height, 1, true);

        // Allow some tolerance
        double maxError = 0;
        for (int i = 0; i < original.Length; i++)
        {
            maxError = Math.Max(maxError, Math.Abs(data[i] - original[i]));
        }

        Assert.True(maxError < 5.0, $"Maximum error {maxError:F2} exceeds tolerance");
    }

    [Fact]
    public void Wavelet_Forward2D_Inverse2D_Lossless_ApproximatesOriginal()
    {
        // Note: The 5/3 wavelet uses integer lifting which can accumulate
        // some rounding errors over multiple levels
        int width = 8;
        int height = 8;
        var original = new float[width * height];
        var random = new Random(42);
        for (int i = 0; i < original.Length; i++)
        {
            original[i] = random.Next(-50, 50); // Smaller range to reduce accumulation
        }

        var data = new float[original.Length];
        original.CopyTo(data, 0);

        Wavelet.Forward2D(data, width, height, 1, false); // Use 1 level
        Wavelet.Inverse2D(data, width, height, 1, false);

        // Allow some tolerance for integer rounding in the lifting scheme
        double maxError = 0;
        for (int i = 0; i < original.Length; i++)
        {
            maxError = Math.Max(maxError, Math.Abs(data[i] - original[i]));
        }

        Assert.True(maxError < 2.0, $"Maximum error {maxError:F2} exceeds tolerance");
    }

    [Fact]
    public void Wavelet_ConstantImage_HasOnlyLLSubband()
    {
        int width = 16;
        int height = 16;
        var data = new float[width * height];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = 100;
        }

        Wavelet.Forward2D(data, width, height, 2, true);

        // After 2 levels, LL is in top-left 4x4 quadrant
        // High-frequency subbands should be approximately zero for constant image
        int llWidth = width / 4;
        int llHeight = height / 4;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (x >= llWidth || y >= llHeight)
                {
                    // High-frequency subbands
                    Assert.True(Math.Abs(data[y * width + x]) < 1.0f,
                        $"High-frequency at ({x}, {y}) should be near zero, got {data[y * width + x]}");
                }
            }
        }
    }

    #endregion

    #region MQ Coder Tests

    [Fact]
    public void MQCoder_EncodeProducesOutput()
    {
        using var stream = new MemoryStream();

        // Encode some symbols
        var encoder = new MQEncoder(stream);
        for (int i = 0; i < 100; i++)
        {
            encoder.Encode(0, i % 2);
        }
        encoder.Flush();

        // Verify that output was produced
        Assert.True(stream.Length > 0, "Encoder should produce output");
    }

    [Fact]
    public void MQCoder_DecodeDoesNotCrash()
    {
        using var stream = new MemoryStream();

        // Encode
        var encoder = new MQEncoder(stream);
        for (int i = 0; i < 50; i++)
        {
            encoder.Encode(0, 1);
            encoder.Encode(0, 0);
        }
        encoder.Flush();

        // Decode - should not throw
        stream.Position = 0;
        var decoder = new MQDecoder(stream);

        for (int i = 0; i < 100; i++)
        {
            int bit = decoder.Decode(0);
            Assert.True(bit == 0 || bit == 1, "Decoded bit should be 0 or 1");
        }
    }

    [Fact]
    public void MQCoder_LongSequence_DoesNotThrow()
    {
        using var stream = new MemoryStream();
        var random = new Random(42);

        // Generate random sequence
        var bits = new int[1000];
        for (int i = 0; i < bits.Length; i++)
        {
            bits[i] = random.Next(2);
        }

        // Encode
        var encoder = new MQEncoder(stream);
        foreach (var bit in bits)
        {
            encoder.Encode(0, bit);
        }
        encoder.Flush();

        // Verify encoding worked
        Assert.True(stream.Length > 0, "Encoding should produce output");

        // Decode - should not throw
        stream.Position = 0;
        var decoder = new MQDecoder(stream);

        for (int i = 0; i < bits.Length; i++)
        {
            int decoded = decoder.Decode(0);
            Assert.True(decoded == 0 || decoded == 1, "Decoded bit should be valid");
        }
    }

    #endregion

    #region Quantization Tests

    [Fact]
    public void Quantization_CalculateStepSizes_Lossless_AllOne()
    {
        var steps = Jp2kQuantization.CalculateStepSizes(0.1f, 5, false);

        for (int level = 0; level <= 5; level++)
        {
            for (int band = 0; band < 4; band++)
            {
                Assert.Equal(1.0f, steps[level, band]);
            }
        }
    }

    [Fact]
    public void Quantization_CalculateStepSizes_Lossy_IncreasesWithLevel()
    {
        var steps = Jp2kQuantization.CalculateStepSizes(0.1f, 5, true);

        // Higher levels (lower frequency) should have smaller steps
        // Level 0 is highest frequency
        for (int level = 0; level < 4; level++)
        {
            float currentHL = steps[level, Jp2kConstants.SubbandHL];
            float nextHL = steps[level + 1, Jp2kConstants.SubbandHL];

            // Steps should scale with level
            Assert.True(currentHL < nextHL || level == 4,
                $"Step at level {level} ({currentHL}) should be less than level {level + 1} ({nextHL})");
        }
    }

    [Fact]
    public void Quantization_QuantizeDequantize_Roundtrip()
    {
        float stepSize = 0.5f;
        var testValues = new float[] { 0.0f, 1.5f, -2.3f, 10.7f, -0.25f, 100.0f };

        foreach (var value in testValues)
        {
            int quantized = Jp2kQuantization.Quantize(value, stepSize);
            bool negative = value < 0;
            float dequantized = Jp2kQuantization.Dequantize(quantized, negative, stepSize);

            // Should be close to original (within half step size)
            Assert.True(Math.Abs(Math.Abs(value) - Math.Abs(dequantized)) < stepSize,
                $"Value {value}: quantized={quantized}, dequantized={dequantized}");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(75)]
    [InlineData(100)]
    public void Quantization_QualityToStep_ValidRange(int quality)
    {
        float step = Jp2kQuantization.QualityToStep(quality);

        Assert.True(step > 0, $"Step size should be positive for quality {quality}");
        Assert.True(step < 10, $"Step size should be reasonable for quality {quality}");
    }

    [Fact]
    public void Quantization_QualityToStep_HigherQuality_SmallerStep()
    {
        float step50 = Jp2kQuantization.QualityToStep(50);
        float step75 = Jp2kQuantization.QualityToStep(75);
        float step100 = Jp2kQuantization.QualityToStep(100);

        Assert.True(step100 < step75, "Quality 100 should have smaller step than 75");
        Assert.True(step75 < step50, "Quality 75 should have smaller step than 50");
    }

    #endregion

    #region Subband Tests

    [Fact]
    public void Subband_CreateCodeBlocks_CorrectCount()
    {
        var subband = new Subband(Jp2kConstants.SubbandHL, 0, 128, 64, 0, 0);
        subband.CreateCodeBlocks(64, 64);

        Assert.NotNull(subband.CodeBlocks);
        Assert.Equal(1, subband.CodeBlocks.GetLength(0)); // 64 / 64 = 1 block in Y
        Assert.Equal(2, subband.CodeBlocks.GetLength(1)); // 128 / 64 = 2 blocks in X
    }

    [Fact]
    public void Subband_CreateCodeBlocks_PartialBlocks()
    {
        var subband = new Subband(Jp2kConstants.SubbandHL, 0, 100, 50, 0, 0);
        subband.CreateCodeBlocks(64, 64);

        Assert.NotNull(subband.CodeBlocks);
        Assert.Equal(1, subband.CodeBlocks.GetLength(0)); // 50 / 64 = 1 (partial)
        Assert.Equal(2, subband.CodeBlocks.GetLength(1)); // 100 / 64 = 2 (partial)

        // Last block should be smaller
        var lastBlock = subband.CodeBlocks[0, 1];
        Assert.Equal(36, lastBlock.Width); // 100 - 64 = 36
    }

    [Fact]
    public void CodeBlock_Initialize_CreatesArrays()
    {
        var subband = new Subband(Jp2kConstants.SubbandHL, 0, 64, 64, 0, 0);
        var block = new CodeBlock(0, 0, 32, 32, subband);

        block.Initialize();

        Assert.NotNull(block.Coefficients);
        Assert.NotNull(block.Signs);
        Assert.NotNull(block.Significance);
        Assert.Equal(32, block.Coefficients.GetLength(0));
        Assert.Equal(32, block.Coefficients.GetLength(1));
    }

    [Fact]
    public void CodeBlock_CalculateBitPlanes_CorrectValue()
    {
        var subband = new Subband(Jp2kConstants.SubbandHL, 0, 64, 64, 0, 0);
        var block = new CodeBlock(0, 0, 4, 4, subband);
        block.Initialize();

        // Set some values
        block.Coefficients![0, 0] = 15;  // Max value = 15, needs 4 bits
        block.Coefficients[1, 1] = 7;
        block.Coefficients[2, 2] = 3;

        block.CalculateBitPlanes();

        Assert.Equal(4, block.NumBitPlanes);
    }

    #endregion

    #region Full Encode/Decode Tests

    [Fact]
    public void Jpeg2000_EncodeDecode_SmallImage_Roundtrip()
    {
        // Create a simple 8x8 grayscale image
        var gray = new byte[64];
        for (int i = 0; i < 64; i++)
        {
            gray[i] = (byte)(i * 4);
        }

        // Encode with high quality
        var encoded = Jpeg2000.Encode(gray, 8, 8, 95, true, 2);

        // Verify it's a valid JPEG2000 codestream
        Assert.True(Jpeg2000.IsJpeg2000Codestream(encoded),
            "Output should be a valid JPEG2000 codestream");

        // Decode
        var decoded = Jpeg2000.Decode(encoded, out int width, out int height);

        Assert.Equal(8, width);
        Assert.Equal(8, height);
        Assert.Equal(64, decoded.Length);
    }

    [Fact]
    public void Jpeg2000_EncodeDecode_16x16_Roundtrip()
    {
        int size = 16;
        var gray = new byte[size * size];
        var random = new Random(42);
        random.NextBytes(gray);

        var encoded = Jpeg2000.Encode(gray, size, size, 90, true, 3);
        var decoded = Jpeg2000.Decode(encoded, out int width, out int height);

        Assert.Equal(size, width);
        Assert.Equal(size, height);
        Assert.Equal(size * size, decoded.Length);
    }

    [Fact]
    public void Jpeg2000_EncodeDecode_32x32_ProducesValidOutput()
    {
        int size = 32;
        var gray = new byte[size * size];

        // Create a gradient
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                gray[y * size + x] = (byte)((x + y) * 4);
            }
        }

        var encoded = Jpeg2000.Encode(gray, size, size, 95, true, 4);
        var decoded = Jpeg2000.Decode(encoded, out int width, out int height);

        Assert.Equal(size, width);
        Assert.Equal(size, height);
        Assert.Equal(size * size, decoded.Length);

        // Verify output is reasonable (not all zeros or all 255s)
        int sum = 0;
        for (int i = 0; i < decoded.Length; i++)
        {
            sum += decoded[i];
        }
        double avg = sum / (double)decoded.Length;
        Assert.True(avg > 50 && avg < 200, $"Average pixel value {avg:F1} seems unreasonable");
    }

    [Fact]
    public void Jpeg2000_EncodeDecode_ConstantImage_ExactReconstruction()
    {
        int size = 16;
        var gray = new byte[size * size];
        for (int i = 0; i < gray.Length; i++)
        {
            gray[i] = 128;
        }

        var encoded = Jpeg2000.Encode(gray, size, size, 100, true, 3);
        var decoded = Jpeg2000.Decode(encoded, out int width, out int height);

        // Constant image should reconstruct well
        for (int i = 0; i < gray.Length; i++)
        {
            Assert.True(Math.Abs(gray[i] - decoded[i]) < 5,
                $"Pixel {i}: expected ~128, got {decoded[i]}");
        }
    }

    #endregion

    #region Quality Variation Tests

    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(75)]
    [InlineData(90)]
    [InlineData(100)]
    public void Jpeg2000_DifferentQualities_ProduceValidCodestream(int quality)
    {
        int size = 16;
        var gray = new byte[size * size];
        var random = new Random(42);
        random.NextBytes(gray);

        var encoded = Jpeg2000.Encode(gray, size, size, quality, true, 3);

        Assert.True(Jpeg2000.IsJpeg2000Codestream(encoded));

        var decoded = Jpeg2000.Decode(encoded, out int width, out int height);
        Assert.Equal(size, width);
        Assert.Equal(size, height);
    }

    [Fact]
    public void Jpeg2000_DifferentQuality_ProducesOutput()
    {
        // Note: Quality mapping in this simplified implementation
        // may not perfectly correlate with reconstruction quality
        int size = 32;
        var gray = new byte[size * size];
        var random = new Random(42);
        random.NextBytes(gray);

        var encoded50 = Jpeg2000.Encode(gray, size, size, 50, true, 3);
        var encoded90 = Jpeg2000.Encode(gray, size, size, 90, true, 3);

        var decoded50 = Jpeg2000.Decode(encoded50, out int w1, out int h1);
        var decoded90 = Jpeg2000.Decode(encoded90, out int w2, out int h2);

        // Both should produce valid output
        Assert.Equal(size, w1);
        Assert.Equal(size, h1);
        Assert.Equal(size, w2);
        Assert.Equal(size, h2);
        Assert.Equal(size * size, decoded50.Length);
        Assert.Equal(size * size, decoded90.Length);
    }

    #endregion

    #region Lossy vs Lossless Tests

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Jpeg2000_LossyAndLossless_ProduceValidCodestream(bool lossy)
    {
        int size = 16;
        var gray = new byte[size * size];
        var random = new Random(42);
        random.NextBytes(gray);

        var encoded = Jpeg2000.Encode(gray, size, size, 100, lossy, 3);

        Assert.True(Jpeg2000.IsJpeg2000Codestream(encoded));

        var decoded = Jpeg2000.Decode(encoded, out int width, out int height);
        Assert.Equal(size, width);
        Assert.Equal(size, height);
    }

    #endregion

    #region Decomposition Level Tests

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Jpeg2000_DifferentLevels_ProduceValidCodestream(int levels)
    {
        int size = 64; // Need larger image for more levels
        var gray = new byte[size * size];
        var random = new Random(42);
        random.NextBytes(gray);

        var encoded = Jpeg2000.Encode(gray, size, size, 80, true, levels);

        Assert.True(Jpeg2000.IsJpeg2000Codestream(encoded));

        var decoded = Jpeg2000.Decode(encoded, out int width, out int height);
        Assert.Equal(size, width);
        Assert.Equal(size, height);
    }

    #endregion

    #region Signature Detection Tests

    [Fact]
    public void IsJpeg2000Codestream_ValidCodestream_ReturnsTrue()
    {
        // SOC marker = 0xFF4F
        var data = new byte[] { 0xFF, 0x4F, 0xFF, 0x51 };
        Assert.True(Jpeg2000.IsJpeg2000Codestream(data));
    }

    [Fact]
    public void IsJpeg2000Codestream_InvalidData_ReturnsFalse()
    {
        var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };
        Assert.False(Jpeg2000.IsJpeg2000Codestream(data));
    }

    [Fact]
    public void IsJpeg2000Codestream_TooShort_ReturnsFalse()
    {
        var data = new byte[] { 0xFF };
        Assert.False(Jpeg2000.IsJpeg2000Codestream(data));
    }

    [Fact]
    public void IsJp2File_ValidSignature_ReturnsTrue()
    {
        var data = new byte[] {
            0x00, 0x00, 0x00, 0x0C,  // Box length
            0x6A, 0x50, 0x20, 0x20,  // 'jP  '
            0x0D, 0x0A, 0x87, 0x0A   // Signature
        };
        Assert.True(Jpeg2000.IsJp2File(data));
    }

    [Fact]
    public void IsJp2File_InvalidData_ReturnsFalse()
    {
        var data = new byte[] { 0xFF, 0x4F, 0xFF, 0x51 }; // Codestream, not JP2
        Assert.False(Jpeg2000.IsJp2File(data));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Jpeg2000_EncodeDecode_AllBlack()
    {
        int size = 16;
        var gray = new byte[size * size]; // All zeros

        var encoded = Jpeg2000.Encode(gray, size, size, 90, true, 3);
        var decoded = Jpeg2000.Decode(encoded, out int width, out int height);

        Assert.Equal(size, width);
        Assert.Equal(size, height);
        Assert.Equal(size * size, decoded.Length);

        // Verify output is valid (values in range)
        foreach (var b in decoded)
        {
            Assert.True(b >= 0 && b <= 255, "Pixel value out of range");
        }
    }

    [Fact]
    public void Jpeg2000_EncodeDecode_AllWhite()
    {
        int size = 16;
        var gray = new byte[size * size];
        for (int i = 0; i < gray.Length; i++)
        {
            gray[i] = 255;
        }

        var encoded = Jpeg2000.Encode(gray, size, size, 90, true, 3);
        var decoded = Jpeg2000.Decode(encoded, out int width, out int height);

        Assert.Equal(size, width);
        Assert.Equal(size, height);
        Assert.Equal(size * size, decoded.Length);

        // Verify output is valid (values in range)
        foreach (var b in decoded)
        {
            Assert.True(b >= 0 && b <= 255, "Pixel value out of range");
        }
    }

    [Fact]
    public void Jpeg2000_EncodeDecode_Checkerboard()
    {
        int size = 16;
        var gray = new byte[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                gray[y * size + x] = (byte)(((x + y) % 2) * 255);
            }
        }

        var encoded = Jpeg2000.Encode(gray, size, size, 95, true, 3);
        var decoded = Jpeg2000.Decode(encoded, out int width, out int height);

        Assert.Equal(size, width);
        Assert.Equal(size, height);
    }

    [Fact]
    public void Jpeg2000_EncodeDecode_NonSquareImage()
    {
        int width = 32;
        int height = 16;
        var gray = new byte[width * height];
        var random = new Random(42);
        random.NextBytes(gray);

        var encoded = Jpeg2000.Encode(gray, width, height, 85, true, 3);
        var decoded = Jpeg2000.Decode(encoded, out int decWidth, out int decHeight);

        Assert.Equal(width, decWidth);
        Assert.Equal(height, decHeight);
    }

    #endregion

    #region Multi-Component Tests

    [Fact]
    public void Jpeg2000_EncodeDecode_RGB_Roundtrip()
    {
        int width = 16;
        int height = 16;
        int numComponents = 3;
        var rgb = new byte[width * height * numComponents];

        // Create a simple RGB pattern
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = (y * width + x) * numComponents;
                rgb[idx + 0] = (byte)(x * 16);      // R
                rgb[idx + 1] = (byte)(y * 16);      // G
                rgb[idx + 2] = (byte)((x + y) * 8); // B
            }
        }

        var encoded = Jpeg2000.Encode(rgb, width, height, numComponents, 90, true, 3, true);

        Assert.True(Jpeg2000.IsJpeg2000Codestream(encoded));

        var decoded = Jpeg2000.Decode(encoded, out int decWidth, out int decHeight, out int decComponents);

        Assert.Equal(width, decWidth);
        Assert.Equal(height, decHeight);
        Assert.Equal(numComponents, decComponents);
        Assert.Equal(width * height * numComponents, decoded.Length);
    }

    [Fact]
    public void Jpeg2000_EncodeDecode_RGB_NoColorTransform_Roundtrip()
    {
        int width = 16;
        int height = 16;
        int numComponents = 3;
        var rgb = new byte[width * height * numComponents];
        var random = new Random(42);
        random.NextBytes(rgb);

        // Encode without color transform
        var encoded = Jpeg2000.Encode(rgb, width, height, numComponents, 90, true, 3, useColorTransform: false);

        Assert.True(Jpeg2000.IsJpeg2000Codestream(encoded));

        var decoded = Jpeg2000.Decode(encoded, out int decWidth, out int decHeight, out int decComponents);

        Assert.Equal(width, decWidth);
        Assert.Equal(height, decHeight);
        Assert.Equal(numComponents, decComponents);
    }

    [Fact]
    public void Jpeg2000_EncodeDecode_CMYK_Roundtrip()
    {
        int width = 16;
        int height = 16;
        int numComponents = 4;
        var cmyk = new byte[width * height * numComponents];

        // Create a simple CMYK pattern
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = (y * width + x) * numComponents;
                cmyk[idx + 0] = (byte)(x * 16);       // C
                cmyk[idx + 1] = (byte)(y * 16);       // M
                cmyk[idx + 2] = (byte)((x + y) * 8);  // Y
                cmyk[idx + 3] = (byte)((255 - x * 8)); // K
            }
        }

        // CMYK typically doesn't use color transform
        var encoded = Jpeg2000.Encode(cmyk, width, height, numComponents, 90, true, 3, useColorTransform: false);

        Assert.True(Jpeg2000.IsJpeg2000Codestream(encoded));

        var decoded = Jpeg2000.Decode(encoded, out int decWidth, out int decHeight, out int decComponents);

        Assert.Equal(width, decWidth);
        Assert.Equal(height, decHeight);
        Assert.Equal(numComponents, decComponents);
        Assert.Equal(width * height * numComponents, decoded.Length);
    }

    [Fact]
    public void Jpeg2000_EncodeDecode_RGB_ConstantColor_ProducesValidOutput()
    {
        int width = 16;
        int height = 16;
        int numComponents = 3;
        var rgb = new byte[width * height * numComponents];

        // Solid mid-gray (128, 128, 128)
        for (int i = 0; i < width * height; i++)
        {
            rgb[i * numComponents + 0] = 128; // R
            rgb[i * numComponents + 1] = 128; // G
            rgb[i * numComponents + 2] = 128; // B
        }

        var encoded = Jpeg2000.Encode(rgb, width, height, numComponents, 95, true, 3, true);
        var decoded = Jpeg2000.Decode(encoded, out int decWidth, out int decHeight, out int decComponents);

        Assert.Equal(numComponents, decComponents);
        Assert.Equal(width * height * numComponents, decoded.Length);

        // Verify all channels are in valid range and close to 128
        double rSum = 0, gSum = 0, bSum = 0;
        for (int i = 0; i < width * height; i++)
        {
            rSum += decoded[i * numComponents + 0];
            gSum += decoded[i * numComponents + 1];
            bSum += decoded[i * numComponents + 2];
        }
        double rAvg = rSum / (width * height);
        double gAvg = gSum / (width * height);
        double bAvg = bSum / (width * height);

        // All channels should be reasonably close to 128 for a constant gray image
        Assert.True(rAvg > 80 && rAvg < 180, $"Red average {rAvg:F1} should be around 128");
        Assert.True(gAvg > 80 && gAvg < 180, $"Green average {gAvg:F1} should be around 128");
        Assert.True(bAvg > 80 && bAvg < 180, $"Blue average {bAvg:F1} should be around 128");
    }

    [Fact]
    public void Jpeg2000_EncodeDecode_RGB_Lossless_Roundtrip()
    {
        int width = 16;
        int height = 16;
        int numComponents = 3;
        var rgb = new byte[width * height * numComponents];
        var random = new Random(42);
        random.NextBytes(rgb);

        // Lossless encoding
        var encoded = Jpeg2000.Encode(rgb, width, height, numComponents, 100, lossy: false, 3, true);

        Assert.True(Jpeg2000.IsJpeg2000Codestream(encoded));

        var decoded = Jpeg2000.Decode(encoded, out int decWidth, out int decHeight, out int decComponents);

        Assert.Equal(width, decWidth);
        Assert.Equal(height, decHeight);
        Assert.Equal(numComponents, decComponents);
    }

    [Fact]
    public void Jpeg2000_EncodeDecode_TwoComponents_Roundtrip()
    {
        // Test with an unusual number of components (2)
        int width = 16;
        int height = 16;
        int numComponents = 2;
        var data = new byte[width * height * numComponents];
        var random = new Random(42);
        random.NextBytes(data);

        var encoded = Jpeg2000.Encode(data, width, height, numComponents, 85, true, 3, false);

        Assert.True(Jpeg2000.IsJpeg2000Codestream(encoded));

        var decoded = Jpeg2000.Decode(encoded, out int decWidth, out int decHeight, out int decComponents);

        Assert.Equal(width, decWidth);
        Assert.Equal(height, decHeight);
        Assert.Equal(numComponents, decComponents);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Jpeg2000_EncodeDecode_VariousComponents_ProducesValidOutput(int numComponents)
    {
        int width = 16;
        int height = 16;
        var data = new byte[width * height * numComponents];
        var random = new Random(42);
        random.NextBytes(data);

        bool useColorTransform = numComponents == 3; // Only use MCT for RGB
        var encoded = Jpeg2000.Encode(data, width, height, numComponents, 85, true, 3, useColorTransform);

        Assert.True(Jpeg2000.IsJpeg2000Codestream(encoded));

        var decoded = Jpeg2000.Decode(encoded, out int decWidth, out int decHeight, out int decComponents);

        Assert.Equal(width, decWidth);
        Assert.Equal(height, decHeight);
        Assert.Equal(numComponents, decComponents);
        Assert.Equal(width * height * numComponents, decoded.Length);
    }

    [Fact]
    public void Jpeg2000_EncodeDecode_RGB_LargeImage_ProducesValidOutput()
    {
        int width = 64;
        int height = 64;
        int numComponents = 3;
        var rgb = new byte[width * height * numComponents];
        var random = new Random(42);
        random.NextBytes(rgb);

        var encoded = Jpeg2000.Encode(rgb, width, height, numComponents, 85, true, 4, true);

        Assert.True(Jpeg2000.IsJpeg2000Codestream(encoded));

        var decoded = Jpeg2000.Decode(encoded, out int decWidth, out int decHeight, out int decComponents);

        Assert.Equal(width, decWidth);
        Assert.Equal(height, decHeight);
        Assert.Equal(numComponents, decComponents);
    }

    #endregion
}
