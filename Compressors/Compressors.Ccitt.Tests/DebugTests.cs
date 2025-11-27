using System;
using Xunit;
using Xunit.Abstractions;

namespace Compressors.Ccitt.Tests
{
    public class DebugTests
    {
        private readonly ITestOutputHelper _output;

        public DebugTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Debug_Group4_AllBlack_TraceCompression()
        {
            int width = 16;
            int height = 2;
            int bytesPerRow = width / 8;
            var original = new byte[bytesPerRow * height];

            // Fill with all black (all 1s)
            for (int i = 0; i < original.Length; i++)
            {
                original[i] = 0xFF;
            }

            _output.WriteLine($"Original data ({original.Length} bytes): {BitConverter.ToString(original)}");

            // Use options without EOFB to simplify debugging
            var options = new CcittOptions
            {
                Group = CcittGroup.Group4,
                Width = width,
                EndOfBlock = false // No EOFB
            };

            var encoder = new CcittEncoder(options);
            var compressed = encoder.Encode(original, height);
            _output.WriteLine($"Compressed data ({compressed.Length} bytes): {BitConverter.ToString(compressed)}");

            string bits = string.Join("", Array.ConvertAll(compressed, b => Convert.ToString(b, 2).PadLeft(8, '0')));
            _output.WriteLine($"Compressed bits ({bits.Length}): {bits}");

            // Parse the bits manually
            _output.WriteLine("\nParsing compressed bits:");
            _output.WriteLine($"Expected row 1: 001 (H) + 00110101 (W0) + 0000010111 (B16) = 21 bits");
            _output.WriteLine($"Expected row 2: 1 (V0) + 1 (V0) = 2 bits");
            _output.WriteLine($"Total expected: 23 bits");

            options.Height = height;
            var decoder = new CcittDecoder(options);
            var decompressed = decoder.Decode(compressed);
            _output.WriteLine($"\nDecompressed data ({decompressed.Length} bytes): {BitConverter.ToString(decompressed)}");

            // Check what we got
            for (int i = 0; i < Math.Min(10, decompressed.Length); i++)
            {
                _output.WriteLine($"Byte {i}: expected {original[i]:X2}, got {decompressed[i]:X2}");
            }
        }

        [Fact]
        public void Debug_Group4_VerticalStripes()
        {
            // Simpler test: just 32 pixels wide, 1 row
            // Using BlackIs1=true so bit=1 means black
            int width = 32;
            int height = 1;
            int stripeWidth = 8;
            int bytesPerRow = width / 8;
            var original = new byte[bytesPerRow * height];

            // Create striped pattern: W W W W W W W W | B B B B B B B B | W W W W W W W W | B B B B B B B B
            // Bytes: 00 FF 00 FF (when BlackIs1=true)
            for (int col = 0; col < width; col++)
            {
                bool isBlack = (col / stripeWidth) % 2 == 1;
                if (isBlack)
                {
                    int byteIndex = col / 8;
                    int bitIndex = 7 - (col % 8);
                    original[byteIndex] |= (byte)(1 << bitIndex);
                }
            }

            _output.WriteLine($"Original: {BitConverter.ToString(original)}");
            _output.WriteLine($"Pattern: 8 white, 8 black, 8 white, 8 black");
            _output.WriteLine($"Changing elements at positions: 8, 16, 24, 32(end)");

            // Encode without EOFB for simpler analysis
            // Use BlackIs1=true since we encoded bit=1 as black
            var options = new CcittOptions
            {
                Group = CcittGroup.Group4,
                Width = width,
                EndOfBlock = false,
                BlackIs1 = true
            };

            var encoder = new CcittEncoder(options);
            var compressed = encoder.Encode(original, height);

            string bits = string.Join("", Array.ConvertAll(compressed, b => Convert.ToString(b, 2).PadLeft(8, '0')));
            _output.WriteLine($"\nCompressed ({compressed.Length} bytes): {BitConverter.ToString(compressed)}");
            _output.WriteLine($"Bits: {bits}");

            // Manual analysis of expected encoding for row 1 (ref = all white):
            // a0=-1, a0Color=white, ref=all white
            // a1=8 (first black), b1=32 (no black on ref), offset=8-32=-24, use horizontal
            // Horizontal: 001 + white(8) + black(8)
            // Then a0=16, a0Color=white (after horizontal mode)
            // a1=24 (next black), b1=32, offset=-8, use horizontal
            // Horizontal: 001 + white(8) + black(8)
            // Then a0=32, done

            _output.WriteLine("\nExpected encoding:");
            _output.WriteLine("H mode: 001");
            _output.WriteLine("White 8: 10011 (5 bits)");
            _output.WriteLine("Black 8: 000101 (6 bits)");
            _output.WriteLine("Then repeat for second pair...");

            options.Height = height;
            var decoder = new CcittDecoder(options);
            var decompressed = decoder.Decode(compressed);
            _output.WriteLine($"\nDecompressed: {BitConverter.ToString(decompressed)}");

            for (int i = 0; i < bytesPerRow; i++)
            {
                string status = original[i] == decompressed[i] ? "OK" : "WRONG";
                _output.WriteLine($"Byte {i}: expected {original[i]:X2}, got {decompressed[i]:X2} - {status}");
            }
        }

        [Fact]
        public void Debug_FindChangingElement()
        {
            // Test with all black row (using BlackIs1=true so 0xFF means all black)
            var row = new byte[] { 0xFF, 0xFF }; // 16 bits all set
            int width = 16;

            var options = new CcittOptions { Width = width, BlackIs1 = true };
            var encoder = new CcittEncoder(options);

            // Use reflection to test the private method
            var method = typeof(CcittEncoder).GetMethod("FindChangingElement",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // FindChangingElement(row, after, currentColor)
            // Looking for first element after -1 that differs from white (false)
            int a1 = (int)method.Invoke(encoder, new object[] { row, -1, false });
            _output.WriteLine($"FindChangingElement(allBlack, -1, white, BlackIs1=true) = {a1}");

            // Should be 0 since pixel 0 is black (different from white)
            Assert.Equal(0, a1);
        }

        [Fact]
        public void Debug_GetPixelColor()
        {
            // Test with BlackIs1=true (bit=1 means black)
            var row = new byte[] { 0xFF }; // 8 bits all set
            int width = 8;

            var options = new CcittOptions { Width = width, BlackIs1 = true };
            var encoder = new CcittEncoder(options);

            var method = typeof(CcittEncoder).GetMethod("GetPixelColor",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            for (int i = 0; i < 8; i++)
            {
                bool isBlack = (bool)method.Invoke(encoder, new object[] { row, i });
                _output.WriteLine($"GetPixelColor(0xFF, {i}, BlackIs1=true) = {isBlack}");
                Assert.True(isBlack, $"Pixel {i} should be black when BlackIs1=true and bit=1");
            }

            // Test with BlackIs1=false (bit=0 means black, bit=1 means white)
            var options2 = new CcittOptions { Width = width, BlackIs1 = false };
            var encoder2 = new CcittEncoder(options2);

            for (int i = 0; i < 8; i++)
            {
                bool isBlack = (bool)method.Invoke(encoder2, new object[] { row, i });
                _output.WriteLine($"GetPixelColor(0xFF, {i}, BlackIs1=false) = {isBlack}");
                Assert.False(isBlack, $"Pixel {i} should be white when BlackIs1=false and bit=1");
            }
        }
    }
}
