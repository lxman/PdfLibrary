using Jp2Codec.Tier2;

namespace Jp2Codec.Tests.Tier2
{
    public sealed class LblockIncrementTests
    {
        private static int Read(params int[] bits)
        {
            var w = new PacketHeaderBitWriter();
            foreach (int b in bits) w.WriteBit(b);
            byte[] data = w.ToBytes();
            var r = new PacketHeaderBitReader(data, 0, data.Length);
            return LblockIncrement.Read(r);
        }

        [Fact] public void Codeword_0_DecodesTo_0() => Assert.Equal(0, Read(0));
        [Fact] public void Codeword_10_DecodesTo_1() => Assert.Equal(1, Read(1, 0));
        [Fact] public void Codeword_110_DecodesTo_2() => Assert.Equal(2, Read(1, 1, 0));
        [Fact] public void Codeword_1110_DecodesTo_3() => Assert.Equal(3, Read(1, 1, 1, 0));

        [Fact]
        public void Codeword_11111111110_DecodesTo_10() =>
            Assert.Equal(10, Read(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0));

        [Fact]
        public void RunsPastSanityCap_Throws()
        {
            // 33 ones — no terminating zero.
            var bits = new int[40];
            for (var i = 0; i < 33; i++) bits[i] = 1;
            Assert.Throws<InvalidDataException>(() => Read(bits));
        }
    }
}
