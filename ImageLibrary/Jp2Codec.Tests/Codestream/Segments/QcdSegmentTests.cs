using Jp2Codec.Codestream;
using Jp2Codec.Codestream.Segments;

namespace Jp2Codec.Tests.Codestream.Segments
{
    public sealed class QcdSegmentTests
    {
        private static QcdSegment ParseFrom(HeaderBytes h)
        {
            var r = new CodestreamReader(h.ToArray());
            r.ReadMarker();
            return QcdSegment.Parse(r.ReadSegment());
        }

        [Fact]
        public void Parse_ReversibleWithFiveLevels_HasSixteenExponents()
        {
            QcdSegment qcd = ParseFrom(new HeaderBytes().QcdReversible(decompositionLevels: 5, exponent: 8, guardBits: 2));
            Assert.Equal(QuantizationStyle.None, qcd.Style);
            Assert.Equal(2, qcd.GuardBits);
            // 3 * 5 + 1 = 16 subbands
            Assert.Equal(16, qcd.Exponents.Length);
            Assert.Empty(qcd.Mantissas);
            foreach (int e in qcd.Exponents) Assert.Equal(8, e);
        }

        [Fact]
        public void Parse_ScalarDerived_ReturnsSingleEpsilonMantissaPair()
        {
            var h = new HeaderBytes();
            int at = h.BeginSegment(0xFF5C);
            h.U8(0x41); // Sqcd: guard=2 (0x40) | style=1 (derived)
            // Single 16-bit field: epsilon=8 (bits 11..15), mu=42 (bits 0..10)
            h.U16((8 << 11) | 42);
            h.EndSegment(at);

            QcdSegment qcd = ParseFrom(h);
            Assert.Equal(QuantizationStyle.ScalarDerived, qcd.Style);
            Assert.Equal(2, qcd.GuardBits);
            Assert.Single(qcd.Exponents);
            Assert.Equal(8, qcd.Exponents[0]);
            Assert.Equal(42, qcd.Mantissas[0]);
        }

        [Fact]
        public void Parse_ScalarExpoundedAllSubbands_OnePairPerSubband()
        {
            var h = new HeaderBytes();
            int at = h.BeginSegment(0xFF5C);
            h.U8(0x42); // Sqcd: guard=2 | style=2 (expounded)
            // 4 subbands worth of (epsilon, mu)
            int[] eps = { 5, 7, 9, 11 };
            int[] mus = { 0, 100, 200, 300 };
            for (var i = 0; i < 4; i++)
                h.U16((eps[i] << 11) | mus[i]);
            h.EndSegment(at);

            QcdSegment qcd = ParseFrom(h);
            Assert.Equal(QuantizationStyle.ScalarExpounded, qcd.Style);
            Assert.Equal(eps, qcd.Exponents);
            Assert.Equal(mus, qcd.Mantissas);
        }

        [Fact]
        public void Parse_ScalarDerivedWithMoreThanOnePair_Throws()
        {
            var h = new HeaderBytes();
            int at = h.BeginSegment(0xFF5C);
            h.U8(0x01).U16(0).U16(0); // style=1 but two pairs of payload
            h.EndSegment(at);
            Assert.Throws<InvalidDataException>(() => ParseFrom(h));
        }

        [Fact]
        public void Parse_ScalarExpoundedOddPayload_Throws()
        {
            var h = new HeaderBytes();
            int at = h.BeginSegment(0xFF5C);
            h.U8(0x02).U16(0).U8(0); // 3 payload bytes - not multiple of 2
            h.EndSegment(at);
            Assert.Throws<InvalidDataException>(() => ParseFrom(h));
        }
    }
}
