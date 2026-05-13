using Jp2Codec.Tier2;

namespace Jp2Codec.Tests.Tier2
{
    public sealed class CodingPassLengthCodeTests
    {
        private static int Decode(params int[] bits)
        {
            var w = new PacketHeaderBitWriter();
            foreach (int b in bits) w.WriteBit(b);
            byte[] data = w.ToBytes();
            var r = new PacketHeaderBitReader(data, 0, data.Length);
            return CodingPassLengthCode.Decode(r);
        }

        [Fact] public void Codeword_0_DecodesTo_1() => Assert.Equal(1, Decode(0));
        [Fact] public void Codeword_10_DecodesTo_2() => Assert.Equal(2, Decode(1, 0));
        [Fact] public void Codeword_1100_DecodesTo_3() => Assert.Equal(3, Decode(1, 1, 0, 0));
        [Fact] public void Codeword_1101_DecodesTo_4() => Assert.Equal(4, Decode(1, 1, 0, 1));
        [Fact] public void Codeword_1110_DecodesTo_5() => Assert.Equal(5, Decode(1, 1, 1, 0));

        // '11 11 xxxxx' branch (xxxxx ∈ 0..30).
        [Fact]
        public void Codeword_1111_00000_DecodesTo_6() =>
            Assert.Equal(6, Decode(1, 1, 1, 1, 0, 0, 0, 0, 0));

        [Fact]
        public void Codeword_1111_11110_DecodesTo_36() =>
            Assert.Equal(36, Decode(1, 1, 1, 1, 1, 1, 1, 1, 0));

        // '11 11 11111 xxxxxxx' branch (xxxxxxx ∈ 0..127).
        [Fact]
        public void Codeword_111111111_0000000_DecodesTo_37() =>
            Assert.Equal(37, Decode(1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0));

        [Fact]
        public void Codeword_111111111_1111111_DecodesTo_164() =>
            Assert.Equal(164, Decode(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1));
    }
}
