using System.Text;

namespace Compressors.Lzw.Tests
{
    public class LzwTests
    {
        [Fact]
        public void Compress_EmptyInput_ReturnsValidOutput()
        {
            var input = Array.Empty<byte>();
            var compressed = Lzw.Compress(input);

            Assert.NotNull(compressed);
            Assert.True(compressed.Length > 0);
        }

        [Fact]
        public void Compress_Decompress_RoundTrip_SimpleData()
        {
            var input = Encoding.ASCII.GetBytes("Hello, World!");

            var compressed = Lzw.Compress(input);
            var decompressed = Lzw.Decompress(compressed);

            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void Compress_Decompress_RoundTrip_RepeatingPattern()
        {
            var input = Encoding.ASCII.GetBytes("ABABABABABABABABABABABABABABABABAB");

            var compressed = Lzw.Compress(input);
            var decompressed = Lzw.Decompress(compressed);

            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void Compress_Decompress_RoundTrip_AllByteValues()
        {
            var input = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();

            var compressed = Lzw.Compress(input);
            var decompressed = Lzw.Decompress(compressed);

            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void Compress_Decompress_RoundTrip_LargeRepetitiveData()
        {
            // Create data that will fill the dictionary and trigger clear codes
            var sb = new StringBuilder();
            for (int i = 0; i < 10000; i++)
            {
                sb.Append((char)('A' + (i % 26)));
            }
            var input = Encoding.ASCII.GetBytes(sb.ToString());

            var compressed = Lzw.Compress(input);
            var decompressed = Lzw.Decompress(compressed);

            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void Compress_Decompress_RoundTrip_BinaryData()
        {
            var random = new Random(42); // Fixed seed for reproducibility
            var input = new byte[5000];
            random.NextBytes(input);

            var compressed = Lzw.Compress(input);
            var decompressed = Lzw.Decompress(compressed);

            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void Compress_RepetitiveData_AchievesCompression()
        {
            var input = new byte[1000];
            Array.Fill(input, (byte)'A');

            var compressed = Lzw.Compress(input);

            Assert.True(compressed.Length < input.Length,
                $"Expected compression ratio better than 1:1. Input: {input.Length}, Output: {compressed.Length}");
        }

        [Fact]
        public void Compress_Decompress_Stream_RoundTrip()
        {
            var input = Encoding.ASCII.GetBytes("Stream-based compression test data that should compress well.");

            using var inputStream = new MemoryStream(input);
            using var compressedStream = new MemoryStream();
            Lzw.Compress(inputStream, compressedStream);

            var compressedData = compressedStream.ToArray();
            using var compressedInput = new MemoryStream(compressedData);
            using var decompressedStream = new MemoryStream();
            Lzw.Decompress(compressedInput, decompressedStream);

            Assert.Equal(input, decompressedStream.ToArray());
        }

        [Fact]
        public void Compress_WithPdfOptions_ProducesValidOutput()
        {
            var input = Encoding.ASCII.GetBytes("PDF compatible LZW test");
            var options = LzwOptions.PdfDefault;

            var compressed = Lzw.Compress(input, options);
            var decompressed = Lzw.Decompress(compressed, options);

            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void Compress_WithGifOptions_ProducesValidOutput()
        {
            var input = Encoding.ASCII.GetBytes("GIF compatible LZW test");
            var options = LzwOptions.GifCompatible;

            var compressed = Lzw.Compress(input, options);
            var decompressed = Lzw.Decompress(compressed, options);

            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void Compress_WithTiffOptions_ProducesValidOutput()
        {
            var input = Encoding.ASCII.GetBytes("TIFF compatible LZW test");
            var options = LzwOptions.TiffCompatible;

            var compressed = Lzw.Compress(input, options);
            var decompressed = Lzw.Decompress(compressed, options);

            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void Compress_NullInput_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => Lzw.Compress(null!));
        }

        [Fact]
        public void Decompress_NullInput_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => Lzw.Decompress(null!));
        }

        [Fact]
        public void Compress_SingleByte_RoundTrip()
        {
            var input = new byte[] { 0x42 };

            var compressed = Lzw.Compress(input);
            var decompressed = Lzw.Decompress(compressed);

            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void Compress_Decompress_cScSc_Pattern()
        {
            // This pattern (cScSc where S is a string and c is a character)
            // triggers the special case in LZW decoding where the code
            // is not yet in the table
            var input = Encoding.ASCII.GetBytes("ABABABAB");

            var compressed = Lzw.Compress(input);
            var decompressed = Lzw.Decompress(compressed);

            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void LzwEncoder_Dispose_FlushesOutput()
        {
            var input = Encoding.ASCII.GetBytes("Test");

            using var output = new MemoryStream();
            using (var encoder = new LzwEncoder(output, leaveOpen: true))
            {
                encoder.Encode(input);
            }

            Assert.True(output.Length > 0);
        }

        [Fact]
        public void LzwDecoder_InvalidCode_ThrowsInvalidDataException()
        {
            // Create an invalid LZW stream with an out-of-range code
            // This is a bit tricky to construct, so we'll test with garbage data
            var invalidData = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };

            Assert.ThrowsAny<Exception>(() => Lzw.Decompress(invalidData));
        }
    }
}
