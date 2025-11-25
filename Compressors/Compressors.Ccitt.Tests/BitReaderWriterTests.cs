using Xunit;

namespace Compressors.Ccitt.Tests
{
    public class BitReaderWriterTests
    {
        [Fact]
        public void BitWriter_WriteSingleBits_ProducesCorrectOutput()
        {
            var writer = new CcittBitWriter();

            // Write 10110001 (0xB1)
            writer.WriteBit(1);
            writer.WriteBit(0);
            writer.WriteBit(1);
            writer.WriteBit(1);
            writer.WriteBit(0);
            writer.WriteBit(0);
            writer.WriteBit(0);
            writer.WriteBit(1);

            var result = writer.ToArray();
            Assert.Single(result);
            Assert.Equal(0xB1, result[0]);
        }

        [Fact]
        public void BitWriter_WriteBits_ProducesCorrectOutput()
        {
            var writer = new CcittBitWriter();

            // Write 0xAB (10101011) as 8 bits
            writer.WriteBits(0xAB, 8);

            var result = writer.ToArray();
            Assert.Single(result);
            Assert.Equal(0xAB, result[0]);
        }

        [Fact]
        public void BitWriter_WritePartialBits_ProducesCorrectOutput()
        {
            var writer = new CcittBitWriter();

            // Write 5 bits: 10110
            writer.WriteBits(0b10110, 5);

            var result = writer.ToArray();
            Assert.Single(result);
            // Should be 10110000 = 0xB0
            Assert.Equal(0xB0, result[0]);
        }

        [Fact]
        public void BitReader_ReadSingleBits_ReturnsCorrectValues()
        {
            var data = new byte[] { 0xB1 }; // 10110001
            var reader = new CcittBitReader(data);

            Assert.Equal(1, reader.ReadBit());
            Assert.Equal(0, reader.ReadBit());
            Assert.Equal(1, reader.ReadBit());
            Assert.Equal(1, reader.ReadBit());
            Assert.Equal(0, reader.ReadBit());
            Assert.Equal(0, reader.ReadBit());
            Assert.Equal(0, reader.ReadBit());
            Assert.Equal(1, reader.ReadBit());
        }

        [Fact]
        public void BitReader_ReadBits_ReturnsCorrectValue()
        {
            var data = new byte[] { 0xAB }; // 10101011
            var reader = new CcittBitReader(data);

            Assert.Equal(0xAB, reader.ReadBits(8));
        }

        [Fact]
        public void BitReader_ReadPartialBits_ReturnsCorrectValue()
        {
            var data = new byte[] { 0xB0 }; // 10110000
            var reader = new CcittBitReader(data);

            Assert.Equal(0b10110, reader.ReadBits(5));
        }

        [Fact]
        public void BitReader_PeekBits_DoesNotAdvancePosition()
        {
            var data = new byte[] { 0xAB };
            var reader = new CcittBitReader(data);

            int peeked = reader.PeekBits(4);
            int read = reader.ReadBits(4);

            Assert.Equal(peeked, read);
            Assert.Equal(0b1010, peeked);
        }

        [Fact]
        public void BitReaderWriter_RoundTrip_PreservesData()
        {
            var writer = new CcittBitWriter();

            // Write various bit patterns
            writer.WriteBits(0b101, 3);
            writer.WriteBits(0b11110000, 8);
            writer.WriteBits(0b1, 1);

            var data = writer.ToArray();
            var reader = new CcittBitReader(data);

            Assert.Equal(0b101, reader.ReadBits(3));
            Assert.Equal(0b11110000, reader.ReadBits(8));
            Assert.Equal(0b1, reader.ReadBits(1));
        }

        [Fact]
        public void BitWriter_WriteEol_WritesCorrectPattern()
        {
            var writer = new CcittBitWriter();
            writer.WriteEol();

            var data = writer.ToArray();
            var reader = new CcittBitReader(data);

            // EOL is 000000000001 (12 bits)
            Assert.Equal(0b000000000001, reader.ReadBits(12));
        }

        [Fact]
        public void BitReader_AlignToByte_SkipsToNextByte()
        {
            var data = new byte[] { 0xFF, 0xAA };
            var reader = new CcittBitReader(data);

            reader.ReadBits(3); // Read 3 bits
            reader.AlignToByte(); // Should skip to byte 1

            Assert.Equal(0xAA, reader.ReadBits(8));
        }

        [Fact]
        public void BitWriter_AlignToByte_PadsWithZeros()
        {
            var writer = new CcittBitWriter();

            writer.WriteBits(0b111, 3);
            writer.AlignToByte();
            writer.WriteBits(0xAA, 8);

            var data = writer.ToArray();
            Assert.Equal(2, data.Length);
            Assert.Equal(0b11100000, data[0]);
            Assert.Equal(0xAA, data[1]);
        }

        [Fact]
        public void BitReader_IsAtEnd_ReturnsTrueWhenExhausted()
        {
            var data = new byte[] { 0xFF };
            var reader = new CcittBitReader(data);

            Assert.False(reader.IsAtEnd);
            reader.ReadBits(8);
            Assert.True(reader.IsAtEnd);
        }

        [Fact]
        public void BitReader_BitsRemaining_ReturnsCorrectCount()
        {
            var data = new byte[] { 0xFF, 0xFF }; // 16 bits
            var reader = new CcittBitReader(data);

            Assert.Equal(16, reader.BitsRemaining);
            reader.ReadBits(5);
            Assert.Equal(11, reader.BitsRemaining);
        }
    }
}
