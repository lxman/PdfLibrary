using System;
using Xunit;

namespace Compressors.Ccitt.Tests
{
    public class CcittCompressionTests
    {
        /// <summary>
        /// Creates a simple test bitmap (alternating lines of white and black).
        /// </summary>
        private byte[] CreateTestBitmap(int width, int height, bool pattern)
        {
            int bytesPerRow = (width + 7) / 8;
            var bitmap = new byte[bytesPerRow * height];

            for (int row = 0; row < height; row++)
            {
                byte fillByte = (pattern && row % 2 == 1) ? (byte)0xFF : (byte)0x00;
                for (int col = 0; col < bytesPerRow; col++)
                {
                    bitmap[row * bytesPerRow + col] = fillByte;
                }
            }

            return bitmap;
        }

        /// <summary>
        /// Creates a bitmap with vertical stripes.
        /// Uses BlackIs1=false convention (bit=0 is black, bit=1 is white).
        /// </summary>
        private byte[] CreateStripedBitmap(int width, int height, int stripeWidth)
        {
            int bytesPerRow = (width + 7) / 8;
            var bitmap = new byte[bytesPerRow * height];

            // Start with all white (all 1s for BlackIs1=false)
            for (int i = 0; i < bitmap.Length; i++)
                bitmap[i] = 0xFF;

            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    bool isBlack = (col / stripeWidth) % 2 == 1;
                    if (isBlack)
                    {
                        // Clear bit for black (BlackIs1=false: bit=0 is black)
                        int byteIndex = row * bytesPerRow + col / 8;
                        int bitIndex = 7 - (col % 8);
                        bitmap[byteIndex] &= (byte)~(1 << bitIndex);
                    }
                }
            }

            return bitmap;
        }

        [Fact]
        public void Group4_RoundTrip_AllWhite()
        {
            int width = 64;
            int height = 10;
            var original = new byte[(width / 8) * height]; // All zeros = all white

            var compressed = Ccitt.CompressGroup4(original, width, height);
            var decompressed = Ccitt.DecompressGroup4(compressed, width, height);

            Assert.Equal(original.Length, decompressed.Length);
            Assert.Equal(original, decompressed);
        }

        [Fact]
        public void Group4_RoundTrip_AllBlack()
        {
            int width = 64;
            int height = 10;
            int bytesPerRow = width / 8;
            var original = new byte[bytesPerRow * height];

            // Fill with all black (all 1s in standard CCITT)
            for (int i = 0; i < original.Length; i++)
            {
                original[i] = 0xFF;
            }

            var compressed = Ccitt.CompressGroup4(original, width, height);
            var decompressed = Ccitt.DecompressGroup4(compressed, width, height);

            Assert.Equal(original.Length, decompressed.Length);
            Assert.Equal(original, decompressed);
        }

        [Fact]
        public void Group4_RoundTrip_AlternatingRows()
        {
            int width = 64;
            int height = 10;
            var original = CreateTestBitmap(width, height, true);

            var compressed = Ccitt.CompressGroup4(original, width, height);
            var decompressed = Ccitt.DecompressGroup4(compressed, width, height);

            Assert.Equal(original.Length, decompressed.Length);
            Assert.Equal(original, decompressed);
        }

        [Fact]
        public void Group4_RoundTrip_VerticalStripes()
        {
            int width = 64;
            int height = 10;
            var original = CreateStripedBitmap(width, height, 8);

            var compressed = Ccitt.CompressGroup4(original, width, height);
            var decompressed = Ccitt.DecompressGroup4(compressed, width, height);

            Assert.Equal(original.Length, decompressed.Length);
            Assert.Equal(original, decompressed);
        }

        [Fact]
        public void Group3_1D_RoundTrip_AllWhite()
        {
            int width = 64;
            int height = 10;
            var original = new byte[(width / 8) * height];

            var compressed = Ccitt.CompressGroup3_1D(original, width, height);
            var decompressed = Ccitt.DecompressGroup3_1D(compressed, width, height);

            Assert.Equal(original.Length, decompressed.Length);
            Assert.Equal(original, decompressed);
        }

        [Fact]
        public void Group3_1D_RoundTrip_AlternatingRows()
        {
            int width = 64;
            int height = 10;
            var original = CreateTestBitmap(width, height, true);

            var compressed = Ccitt.CompressGroup3_1D(original, width, height);
            var decompressed = Ccitt.DecompressGroup3_1D(compressed, width, height);

            Assert.Equal(original.Length, decompressed.Length);
            Assert.Equal(original, decompressed);
        }

        [Fact]
        public void Group4_Compression_ReducesSize_ForRepetitiveData()
        {
            int width = 1728; // Standard fax width
            int height = 100;
            var original = new byte[(width / 8) * height]; // All white

            var compressed = Ccitt.CompressGroup4(original, width, height);

            // All-white data should compress extremely well
            Assert.True(compressed.Length < original.Length / 10,
                $"Compression ratio too low: {original.Length} -> {compressed.Length}");
        }

        [Fact]
        public void CcittOptions_FromPdfK_NegativeK_ReturnsGroup4()
        {
            var options = CcittOptions.FromPdfK(-1, 1728);

            Assert.Equal(CcittGroup.Group4, options.Group);
            Assert.Equal(-1, options.K);
            Assert.Equal(1728, options.Width);
        }

        [Fact]
        public void CcittOptions_FromPdfK_ZeroK_ReturnsGroup3_1D()
        {
            var options = CcittOptions.FromPdfK(0, 1728);

            Assert.Equal(CcittGroup.Group3OneDimensional, options.Group);
            Assert.Equal(0, options.K);
        }

        [Fact]
        public void CcittOptions_FromPdfK_PositiveK_ReturnsGroup3_2D()
        {
            var options = CcittOptions.FromPdfK(4, 1728);

            Assert.Equal(CcittGroup.Group3TwoDimensional, options.Group);
            Assert.Equal(4, options.K);
        }

        [Fact]
        public void HuffmanDecoder_DecodesWhiteRunLength()
        {
            var decoder = new HuffmanDecoder();
            var writer = new CcittBitWriter();

            // Write white run length 10 (code: 00111, 5 bits)
            writer.WriteRunLength(10, true);

            var data = writer.ToArray();
            var reader = new CcittBitReader(data);

            int runLength = decoder.DecodeWhiteRunLength(reader);
            Assert.Equal(10, runLength);
        }

        [Fact]
        public void HuffmanDecoder_DecodesBlackRunLength()
        {
            var decoder = new HuffmanDecoder();
            var writer = new CcittBitWriter();

            // Write black run length 5 (code: 0011, 4 bits)
            writer.WriteRunLength(5, false);

            var data = writer.ToArray();
            var reader = new CcittBitReader(data);

            int runLength = decoder.DecodeBlackRunLength(reader);
            Assert.Equal(5, runLength);
        }

        [Fact]
        public void HuffmanDecoder_DecodesLongRunLength()
        {
            var decoder = new HuffmanDecoder();
            var writer = new CcittBitWriter();

            // Write run length 100 (64 makeup + 36 terminating)
            writer.WriteRunLength(100, true);

            var data = writer.ToArray();
            var reader = new CcittBitReader(data);

            int runLength = decoder.DecodeWhiteRunLength(reader);
            Assert.Equal(100, runLength);
        }

        [Fact]
        public void Decompress_EmptyData_ReturnsEmptyArray()
        {
            var result = Ccitt.Decompress(Array.Empty<byte>());
            Assert.Empty(result);
        }

        [Fact]
        public void Compress_EmptyData_ReturnsEmptyArray()
        {
            var result = Ccitt.Compress(Array.Empty<byte>(), 0);
            Assert.Empty(result);
        }

        [Fact]
        public void TwoDimensionalCodes_CanUseVerticalMode_ValidOffsets()
        {
            Assert.True(TwoDimensionalCodes.CanUseVerticalMode(0));
            Assert.True(TwoDimensionalCodes.CanUseVerticalMode(1));
            Assert.True(TwoDimensionalCodes.CanUseVerticalMode(-1));
            Assert.True(TwoDimensionalCodes.CanUseVerticalMode(3));
            Assert.True(TwoDimensionalCodes.CanUseVerticalMode(-3));
        }

        [Fact]
        public void TwoDimensionalCodes_CanUseVerticalMode_InvalidOffsets()
        {
            Assert.False(TwoDimensionalCodes.CanUseVerticalMode(4));
            Assert.False(TwoDimensionalCodes.CanUseVerticalMode(-4));
            Assert.False(TwoDimensionalCodes.CanUseVerticalMode(100));
        }

        [Theory]
        [InlineData(0, TwoDimensionalCodes.Vertical.V0Code, TwoDimensionalCodes.Vertical.V0Bits)]
        [InlineData(1, TwoDimensionalCodes.Vertical.VR1Code, TwoDimensionalCodes.Vertical.VR1Bits)]
        [InlineData(-1, TwoDimensionalCodes.Vertical.VL1Code, TwoDimensionalCodes.Vertical.VL1Bits)]
        public void TwoDimensionalCodes_TryGetVerticalCode_ReturnsCorrectCode(int offset, int expectedCode, int expectedBits)
        {
            bool result = TwoDimensionalCodes.TryGetVerticalCode(offset, out int code, out int bits);

            Assert.True(result);
            Assert.Equal(expectedCode, code);
            Assert.Equal(expectedBits, bits);
        }
    }
}
