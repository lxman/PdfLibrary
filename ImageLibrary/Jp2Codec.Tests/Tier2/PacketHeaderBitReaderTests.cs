using Jp2Codec.Tier2;

namespace Jp2Codec.Tests.Tier2
{
    public sealed class PacketHeaderBitReaderTests
    {
        [Fact]
        public void ReadBit_DeliversMsbFirst()
        {
            // 0b10110100 = 0xB4. Reads should be 1, 0, 1, 1, 0, 1, 0, 0.
            var r = new PacketHeaderBitReader(new byte[] { 0xB4 }, 0, 1);
            Assert.Equal(1, r.ReadBit());
            Assert.Equal(0, r.ReadBit());
            Assert.Equal(1, r.ReadBit());
            Assert.Equal(1, r.ReadBit());
            Assert.Equal(0, r.ReadBit());
            Assert.Equal(1, r.ReadBit());
            Assert.Equal(0, r.ReadBit());
            Assert.Equal(0, r.ReadBit());
            Assert.True(r.IsAtEnd);
        }

        [Fact]
        public void ReadBits_PacksIntoMultiBitInteger()
        {
            // 0xB4 reads as binary: 1011_0100.  First 4 bits = 0b1011 = 11.
            var r = new PacketHeaderBitReader(new byte[] { 0xB4 }, 0, 1);
            Assert.Equal(0b1011, r.ReadBits(4));
            Assert.Equal(0b0100, r.ReadBits(4));
        }

        [Fact]
        public void ReadBit_AfterFfByte_DropsTopStuffBit()
        {
            // First byte 0xFF (delivers 8 bits 1111_1111), next byte 0x42 — but
            // since the previous byte was 0xFF, only the low 7 bits are usable.
            // 0x42 = 0100_0010 → stuff bit is the top '0', remaining 7 bits = 1000_010
            // which encoder forced top bit to 0 (it had to). So we read 7 bits = 0b1000010 = 66.
            var r = new PacketHeaderBitReader(new byte[] { 0xFF, 0x42 }, 0, 2);
            for (var i = 0; i < 8; i++) Assert.Equal(1, r.ReadBit());
            Assert.Equal(0b1000010, r.ReadBits(7));
            Assert.True(r.IsAtEnd);
        }

        [Fact]
        public void AlignToByte_SkipsRemainingBitsOfCurrentByte()
        {
            var r = new PacketHeaderBitReader(new byte[] { 0xB4, 0x55 }, 0, 2);
            Assert.Equal(1, r.ReadBit());
            r.AlignToByte();
            // Next read should fetch the new byte from position 1.
            Assert.Equal(0, r.ReadBit());                 // 0x55 = 0101_0101 → first bit 0
            Assert.Equal(0b101_0101, r.ReadBits(7));
        }
    }
}
