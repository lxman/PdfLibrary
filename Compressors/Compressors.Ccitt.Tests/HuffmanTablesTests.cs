using Xunit;

namespace Compressors.Ccitt.Tests
{
    public class HuffmanTablesTests
    {
        [Fact]
        public void WhiteTerminatingCodes_HasCorrectCount()
        {
            Assert.Equal(64, HuffmanTables.WhiteTerminatingCodes.Length);
        }

        [Fact]
        public void BlackTerminatingCodes_HasCorrectCount()
        {
            Assert.Equal(64, HuffmanTables.BlackTerminatingCodes.Length);
        }

        [Fact]
        public void WhiteMakeupCodes_HasCorrectCount()
        {
            // 64, 128, 192, ... 1728 = 27 entries
            Assert.Equal(27, HuffmanTables.WhiteMakeupCodes.Length);
        }

        [Fact]
        public void BlackMakeupCodes_HasCorrectCount()
        {
            Assert.Equal(27, HuffmanTables.BlackMakeupCodes.Length);
        }

        [Fact]
        public void ExtendedMakeupCodes_HasCorrectCount()
        {
            // 1792, 1856, ... 2560 = 13 entries
            Assert.Equal(13, HuffmanTables.ExtendedMakeupCodes.Length);
        }

        [Theory]
        [InlineData(0, 0b00110101, 8)]   // White run length 0
        [InlineData(1, 0b000111, 6)]     // White run length 1
        [InlineData(2, 0b0111, 4)]       // White run length 2
        [InlineData(63, 0b00110100, 8)]  // White run length 63
        public void WhiteTerminatingCodes_HasCorrectValues(int index, int expectedCode, int expectedBits)
        {
            var code = HuffmanTables.WhiteTerminatingCodes[index];
            Assert.Equal(expectedCode, code.Code);
            Assert.Equal(expectedBits, code.BitLength);
        }

        [Theory]
        [InlineData(0, 0b0000110111, 10)] // Black run length 0
        [InlineData(1, 0b010, 3)]          // Black run length 1
        [InlineData(2, 0b11, 2)]           // Black run length 2
        [InlineData(3, 0b10, 2)]           // Black run length 3
        public void BlackTerminatingCodes_HasCorrectValues(int index, int expectedCode, int expectedBits)
        {
            var code = HuffmanTables.BlackTerminatingCodes[index];
            Assert.Equal(expectedCode, code.Code);
            Assert.Equal(expectedBits, code.BitLength);
        }

        [Fact]
        public void GetRunLengthCodes_ShortWhiteRun_ReturnsOnlyTerminating()
        {
            HuffmanTables.GetRunLengthCodes(10, true, out var makeup, out var terminating);

            Assert.Equal(0, makeup.BitLength); // No makeup code
            Assert.Equal(HuffmanTables.WhiteTerminatingCodes[10].Code, terminating.Code);
        }

        [Fact]
        public void GetRunLengthCodes_ShortBlackRun_ReturnsOnlyTerminating()
        {
            HuffmanTables.GetRunLengthCodes(5, false, out var makeup, out var terminating);

            Assert.Equal(0, makeup.BitLength); // No makeup code
            Assert.Equal(HuffmanTables.BlackTerminatingCodes[5].Code, terminating.Code);
        }

        [Fact]
        public void GetRunLengthCodes_LongWhiteRun_ReturnsMakeupAndTerminating()
        {
            // 100 = 64 + 36, so makeup for 64, terminating for 36
            HuffmanTables.GetRunLengthCodes(100, true, out var makeup, out var terminating);

            Assert.True(makeup.BitLength > 0); // Should have makeup code
            Assert.Equal(HuffmanTables.WhiteMakeupCodes[0].Code, makeup.Code); // Makeup for 64
            Assert.Equal(HuffmanTables.WhiteTerminatingCodes[36].Code, terminating.Code);
        }

        [Fact]
        public void AllWhiteTerminatingCodes_HaveValidBitLength()
        {
            foreach (var code in HuffmanTables.WhiteTerminatingCodes)
            {
                Assert.True(code.BitLength >= 2 && code.BitLength <= 13,
                    $"Invalid bit length: {code.BitLength}");
            }
        }

        [Fact]
        public void AllBlackTerminatingCodes_HaveValidBitLength()
        {
            foreach (var code in HuffmanTables.BlackTerminatingCodes)
            {
                Assert.True(code.BitLength >= 2 && code.BitLength <= 13,
                    $"Invalid bit length: {code.BitLength}");
            }
        }
    }
}
