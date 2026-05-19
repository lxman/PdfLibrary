using Jp2Codec.Codestream;
using Jp2Codec.Codestream.Segments;

namespace Jp2Codec.Tests.Codestream.Segments
{
    public sealed class SotSegmentTests
    {
        private static SotSegment ParseFrom(HeaderBytes h)
        {
            var r = new CodestreamReader(h.ToArray());
            r.ReadMarker();
            return SotSegment.Parse(r.ReadSegment());
        }

        [Fact]
        public void Parse_AllFieldsRoundTripFromBytes()
        {
            SotSegment sot = ParseFrom(new HeaderBytes().Sot(tileIndex: 3, psot: 12345, tpsot: 1, tnsot: 4));
            Assert.Equal(3, sot.TileIndex);
            Assert.Equal(12345u, sot.TilePartLength);
            Assert.Equal(1, sot.TilePartIndex);
            Assert.Equal(4, sot.TilePartCount);
        }

        [Fact]
        public void Parse_PsotZero_AllowedAsStreamingLength()
        {
            // Spec: Psot = 0 means "tile-part extends to EOC or next SOT".
            SotSegment sot = ParseFrom(new HeaderBytes().Sot(psot: 0));
            Assert.Equal(0u, sot.TilePartLength);
        }

        [Fact]
        public void Parse_WrongPayloadLength_Throws()
        {
            // Build a SOT with only 7 payload bytes instead of 8.
            var h = new HeaderBytes();
            int at = h.BeginSegment(0xFF90);
            h.U16(0).U32(0).U8(0); // 7 bytes
            h.EndSegment(at);
            Assert.Throws<InvalidDataException>(() => ParseFrom(h));
        }
    }
}
