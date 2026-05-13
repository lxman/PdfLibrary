using Jp2Codec.Tier1;

namespace Jp2Codec.Tests.Tier1
{
    public sealed class Tier1RawBitReaderTests
    {
        [Fact]
        public void ReadsBitsMsbFirstFromSimpleByte()
        {
            // 0xA5 = 1010 0101.
            var reader = new Tier1RawBitReader([0xA5], 0, 1);
            Assert.Equal(1, reader.ReadBit());
            Assert.Equal(0, reader.ReadBit());
            Assert.Equal(1, reader.ReadBit());
            Assert.Equal(0, reader.ReadBit());
            Assert.Equal(0, reader.ReadBit());
            Assert.Equal(1, reader.ReadBit());
            Assert.Equal(0, reader.ReadBit());
            Assert.Equal(1, reader.ReadBit());
        }

        [Fact]
        public void SpansBytesWithoutStuffBit()
        {
            // Two bytes neither equal to 0xFF — the second byte uses all 8
            // bits, no stuff-bit skip applies. 0x0F = 0000 1111, 0x33 = 0011 0011.
            var reader = new Tier1RawBitReader([0x0F, 0x33], 0, 2);
            int seen = 0;
            for (var i = 0; i < 16; i++) seen = (seen << 1) | reader.ReadBit();
            Assert.Equal(0x0F33, seen);
        }

        [Fact]
        public void SkipsStuffBitAfterFfByte()
        {
            // 0xFF followed by 0x55 = 0101 0101. The MSB (0) is the stuff
            // bit and is discarded; remaining 7 bits are 101 0101.
            var reader = new Tier1RawBitReader([0xFF, 0x55], 0, 2);

            // First 8 bits: the 0xFF byte itself, MSB-first.
            for (var i = 0; i < 8; i++)
                Assert.Equal(1, reader.ReadBit());

            // Next come 7 bits from the stuffed byte (skipping the MSB).
            int[] expected = [1, 0, 1, 0, 1, 0, 1];
            foreach (int b in expected)
                Assert.Equal(b, reader.ReadBit());
        }

        [Fact]
        public void StuffBitOnlyAppliesOnceAfterFf()
        {
            // Encoder rule: after a 0xFF byte the next byte's MSB is 0
            // (forced stuff bit). Since that next byte therefore has its MSB
            // set to 0 it can never itself be 0xFF — so the stuff rule does
            // not chain. Verify the third byte uses all 8 bits.
            //
            // Bytes: 0xFF, 0x40 (MSB-0 stuff-byte; remaining 7 bits = 100 0000),
            // 0x7E = 0111 1110.
            var reader = new Tier1RawBitReader([0xFF, 0x40, 0x7E], 0, 3);

            // Skip the 8 bits from 0xFF and the 7 from the stuffed byte.
            for (var i = 0; i < 8 + 7; i++) reader.ReadBit();

            // Third byte: 8 full bits.
            int seen = 0;
            for (var i = 0; i < 8; i++) seen = (seen << 1) | reader.ReadBit();
            Assert.Equal(0x7E, seen);
        }

        [Fact]
        public void ReturnsZeroPastEndOfBuffer()
        {
            var reader = new Tier1RawBitReader([0x80], 0, 1);
            Assert.Equal(1, reader.ReadBit());
            for (var i = 0; i < 7; i++) Assert.Equal(0, reader.ReadBit());
            // Now past EOB.
            for (var i = 0; i < 10; i++) Assert.Equal(0, reader.ReadBit());
        }

        [Fact]
        public void RoundTripsBitsWrittenByTier1RawBitWriter()
        {
            // Property-style sanity: a known mixed pattern that crosses the
            // 0xFF boundary survives writer→reader.
            int[] bits =
            [
                1, 1, 1, 1, 1, 1, 1, 1, // 0xFF
                1, 0, 1, 0, 1, 0, 1,    // 7 bits in stuffed byte
                0, 1, 0, 1, 0, 1, 0, 1, // next byte (no stuff): 0x55
            ];

            var w = new Tier1RawBitWriter();
            foreach (int b in bits) w.WriteBit(b);
            w.Flush();

            var r = new Tier1RawBitReader(w.ToArray(), 0, w.ToArray().Length);
            foreach (int b in bits) Assert.Equal(b, r.ReadBit());
        }
    }
}
