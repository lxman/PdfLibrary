using Jp2Codec.Tier2;

namespace Jp2Codec.Tests.Tier2
{
    public sealed class PacketHeaderBitWriterTests
    {
        [Fact]
        public void RoundTrip_BitsMatchInputAfterReaderConsumesThem()
        {
            var w = new PacketHeaderBitWriter();
            // Write 10 bits: 1, 0, 1, 1, 0, 1, 0, 0, 1, 1
            int[] expected = { 1, 0, 1, 1, 0, 1, 0, 0, 1, 1 };
            foreach (int b in expected) w.WriteBit(b);
            byte[] bytes = w.ToBytes();

            var r = new PacketHeaderBitReader(bytes, 0, bytes.Length);
            foreach (int b in expected) Assert.Equal(b, r.ReadBit());
        }

        [Fact]
        public void RoundTrip_StuffBitInsertedAfterFfByte()
        {
            // Force an 0xFF byte by writing 8 ones. After that, a normal payload
            // of 7 bits should be readable via the reader (which itself drops
            // the stuff bit of the following byte).
            var w = new PacketHeaderBitWriter();
            for (var i = 0; i < 8; i++) w.WriteBit(1);
            // payload: 7 distinct bits 1,0,0,0,0,1,0
            int[] payload = { 1, 0, 0, 0, 0, 1, 0 };
            foreach (int b in payload) w.WriteBit(b);
            byte[] bytes = w.ToBytes();

            // First byte must be 0xFF; second byte must have top bit = 0.
            Assert.Equal(0xFF, bytes[0]);
            Assert.Equal(0, (bytes[1] >> 7) & 1);

            var r = new PacketHeaderBitReader(bytes, 0, bytes.Length);
            for (var i = 0; i < 8; i++) Assert.Equal(1, r.ReadBit());
            foreach (int b in payload) Assert.Equal(b, r.ReadBit());
        }

        [Fact]
        public void WriteBits_PacksBitsMostSignificantFirst()
        {
            var w = new PacketHeaderBitWriter();
            w.WriteBits(0b1011, 4);
            w.WriteBits(0b0100, 4);
            byte[] bytes = w.ToBytes();
            Assert.Single(bytes);
            Assert.Equal(0xB4, bytes[0]);
        }

        [Fact]
        public void AlignToByte_PadsCurrentByteWithZeros()
        {
            var w = new PacketHeaderBitWriter();
            w.WriteBit(1);
            w.AlignToByte();
            w.WriteBit(1);
            byte[] bytes = w.ToBytes();
            Assert.Equal(2, bytes.Length);
            Assert.Equal(0x80, bytes[0]);
            Assert.Equal(0x80, bytes[1]);
        }
    }
}
