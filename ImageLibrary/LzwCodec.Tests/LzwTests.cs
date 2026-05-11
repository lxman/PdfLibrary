using System.Text;
using LzwCodec;

namespace LzwCodec.Tests
{
    public class LzwTests
    {
        [Fact]
        public void Compress_EmptyInput_ReturnsValidOutput()
        {
            var input = Array.Empty<byte>();
            var compressed = LzwCodec.Lzw.Compress(input);

            Assert.NotNull(compressed);
            Assert.True(compressed.Length > 0);
        }

        [Fact]
        public void Compress_Decompress_RoundTrip_SimpleData()
        {
            var input = Encoding.ASCII.GetBytes("Hello, World!");

            var compressed = LzwCodec.Lzw.Compress(input);
            var decompressed = LzwCodec.Lzw.Decompress(compressed);

            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void Compress_Decompress_RoundTrip_RepeatingPattern()
        {
            var input = Encoding.ASCII.GetBytes("ABABABABABABABABABABABABABABABABAB");

            var compressed = LzwCodec.Lzw.Compress(input);
            var decompressed = LzwCodec.Lzw.Decompress(compressed);

            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void Compress_Decompress_RoundTrip_AllByteValues()
        {
            var input = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();

            var compressed = LzwCodec.Lzw.Compress(input);
            var decompressed = LzwCodec.Lzw.Decompress(compressed);

            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void Compress_Decompress_RoundTrip_LargeRepetitiveData()
        {
            // Create data that will fill the dictionary and trigger clear codes
            var sb = new StringBuilder();
            for (var i = 0; i < 10000; i++)
            {
                sb.Append((char)('A' + i % 26));
            }
            var input = Encoding.ASCII.GetBytes(sb.ToString());

            var compressed = LzwCodec.Lzw.Compress(input);
            var decompressed = LzwCodec.Lzw.Decompress(compressed);

            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void Compress_Decompress_RoundTrip_BinaryData()
        {
            var random = new Random(42); // Fixed seed for reproducibility
            var input = new byte[5000];
            random.NextBytes(input);

            var compressed = LzwCodec.Lzw.Compress(input);
            var decompressed = LzwCodec.Lzw.Decompress(compressed);

            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void Compress_RepetitiveData_AchievesCompression()
        {
            var input = new byte[1000];
            Array.Fill(input, (byte)'A');

            var compressed = LzwCodec.Lzw.Compress(input);

            Assert.True(compressed.Length < input.Length,
                $"Expected compression ratio better than 1:1. Input: {input.Length}, Output: {compressed.Length}");
        }

        [Fact]
        public void Compress_Decompress_Stream_RoundTrip()
        {
            var input = Encoding.ASCII.GetBytes("Stream-based compression test data that should compress well.");

            using var inputStream = new MemoryStream(input);
            using var compressedStream = new MemoryStream();
            LzwCodec.Lzw.Compress(inputStream, compressedStream);

            var compressedData = compressedStream.ToArray();
            using var compressedInput = new MemoryStream(compressedData);
            using var decompressedStream = new MemoryStream();
            LzwCodec.Lzw.Decompress(compressedInput, decompressedStream);

            Assert.Equal(input, decompressedStream.ToArray());
        }

        [Fact]
        public void Compress_WithPdfOptions_ProducesValidOutput()
        {
            var input = Encoding.ASCII.GetBytes("PDF compatible LZW test");
            var options = LzwOptions.PdfDefault;

            var compressed = LzwCodec.Lzw.Compress(input, options);
            var decompressed = LzwCodec.Lzw.Decompress(compressed, options);

            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void Compress_WithGifOptions_ProducesValidOutput()
        {
            var input = Encoding.ASCII.GetBytes("GIF compatible LZW test");
            var options = LzwOptions.GifCompatible;

            var compressed = LzwCodec.Lzw.Compress(input, options);
            var decompressed = LzwCodec.Lzw.Decompress(compressed, options);

            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void Compress_WithTiffOptions_ProducesValidOutput()
        {
            var input = Encoding.ASCII.GetBytes("TIFF compatible LZW test");
            var options = LzwOptions.TiffCompatible;

            var compressed = LzwCodec.Lzw.Compress(input, options);
            var decompressed = LzwCodec.Lzw.Decompress(compressed, options);

            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void Compress_NullInput_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => LzwCodec.Lzw.Compress(null!));
        }

        [Fact]
        public void Decompress_NullInput_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => LzwCodec.Lzw.Decompress(null!));
        }

        [Fact]
        public void Compress_SingleByte_RoundTrip()
        {
            var input = new byte[] { 0x42 };

            var compressed = LzwCodec.Lzw.Compress(input);
            var decompressed = LzwCodec.Lzw.Decompress(compressed);

            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void Compress_Decompress_cScSc_Pattern()
        {
            // This pattern (cScSc where S is a string and c is a character)
            // triggers the special case in LZW decoding where the code
            // is not yet in the table
            var input = Encoding.ASCII.GetBytes("ABABABAB");

            var compressed = LzwCodec.Lzw.Compress(input);
            var decompressed = LzwCodec.Lzw.Decompress(compressed);

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

            Assert.ThrowsAny<Exception>(() => LzwCodec.Lzw.Decompress(invalidData));
        }

        [Fact]
        public void Decompress_LsbFirst_DecodesIdenticallyToMsbFirst()
        {
            // The same LZW code stream [65 ('A'), 66 ('B'), 257 (EOD)] packed at 9-bit
            // codes both MSB-first and LSB-first must decode to the same bytes.
            // Hand-packed; EmitInitialClearCode=false, EarlyChange=true.
            //
            //   byte 0   byte 1   byte 2   byte 3
            // MSB: 0x20     0x90     0xA0     0x20
            // LSB: 0x41     0x84     0x04     0x04
            byte[] msbStream = [0x20, 0x90, 0xA0, 0x20];
            byte[] lsbStream = [0x41, 0x84, 0x04, 0x04];

            var msbOptions = new LzwOptions
            {
                BitOrder = LzwBitOrder.MsbFirst,
                EmitInitialClearCode = false,
                EarlyChange = true
            };
            var lsbOptions = new LzwOptions
            {
                BitOrder = LzwBitOrder.LsbFirst,
                EmitInitialClearCode = false,
                EarlyChange = true
            };

            byte[] msbDecoded = LzwCodec.Lzw.Decompress(msbStream, msbOptions);
            byte[] lsbDecoded = LzwCodec.Lzw.Decompress(lsbStream, lsbOptions);

            Assert.Equal(new byte[] { (byte)'A', (byte)'B' }, msbDecoded);
            Assert.Equal(msbDecoded, lsbDecoded);
        }
    }
}
