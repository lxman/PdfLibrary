using Jp2Codec.Codestream;

namespace Jp2Codec.Tests.Codestream
{
    public sealed class CodestreamReaderTests
    {
        [Fact]
        public void Position_StartsAtZero_BeforeAnyRead()
        {
            var r = new CodestreamReader([0x01, 0x02, 0x03]);
            Assert.Equal(0, r.Position);
            Assert.Equal(3, r.Remaining);
            Assert.False(r.IsAtEnd);
        }

        [Fact]
        public void ReadByte_AdvancesByOne_PerCall()
        {
            var r = new CodestreamReader([0x01, 0x02, 0x03]);
            Assert.Equal(0x01, r.ReadByte());
            Assert.Equal(1, r.Position);
            Assert.Equal(0x02, r.ReadByte());
            Assert.Equal(2, r.Position);
        }

        [Fact]
        public void ReadByte_AtEnd_ThrowsEndOfStream()
        {
            var r = new CodestreamReader([0x01]);
            r.ReadByte();
            Assert.True(r.IsAtEnd);
            Assert.Throws<EndOfStreamException>(() => r.ReadByte());
        }

        [Fact]
        public void ReadUInt16BigEndian_DecodesHighByteFirst()
        {
            var r = new CodestreamReader([0x12, 0x34, 0xAB, 0xCD]);
            Assert.Equal(0x1234, r.ReadUInt16BigEndian());
            Assert.Equal(0xABCD, r.ReadUInt16BigEndian());
            Assert.True(r.IsAtEnd);
        }

        [Fact]
        public void ReadUInt32BigEndian_DecodesAllFourBytesBigEndian()
        {
            var r = new CodestreamReader([0xDE, 0xAD, 0xBE, 0xEF]);
            Assert.Equal(0xDEADBEEFu, r.ReadUInt32BigEndian());
            Assert.True(r.IsAtEnd);
        }

        [Fact]
        public void ReadUInt16BigEndian_WithOneByteRemaining_Throws()
        {
            var r = new CodestreamReader([0x12]);
            Assert.Throws<EndOfStreamException>(() => r.ReadUInt16BigEndian());
        }

        [Fact]
        public void ReadBytes_ReturnsCopyOfRequestedRange_AndAdvances()
        {
            var r = new CodestreamReader([0x01, 0x02, 0x03, 0x04, 0x05]);
            byte[] got = r.ReadBytes(3);
            Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, got);
            Assert.Equal(3, r.Position);
        }

        [Fact]
        public void ReadSpan_ReturnsViewOverBuffer_WithoutCopy_AndAdvances()
        {
            var r = new CodestreamReader([0x01, 0x02, 0x03, 0x04, 0x05]);
            var span = r.ReadSpan(2);
            Assert.Equal(2, span.Length);
            Assert.Equal(0x01, span[0]);
            Assert.Equal(0x02, span[1]);
            Assert.Equal(2, r.Position);
        }

        [Fact]
        public void PeekUInt16BigEndian_DoesNotAdvanceCursor()
        {
            var r = new CodestreamReader([0xFF, 0x4F, 0xAB]);
            Assert.Equal(0xFF4F, r.PeekUInt16BigEndian());
            Assert.Equal(0, r.Position);
            // Subsequent read still sees the same bytes.
            Assert.Equal(0xFF4F, r.ReadUInt16BigEndian());
            Assert.Equal(2, r.Position);
        }

        [Fact]
        public void Skip_AdvancesCursor()
        {
            var r = new CodestreamReader([0x01, 0x02, 0x03, 0x04]);
            r.Skip(2);
            Assert.Equal(2, r.Position);
            Assert.Equal(0x03, r.ReadByte());
        }

        [Fact]
        public void Skip_PastEnd_Throws()
        {
            var r = new CodestreamReader([0x01, 0x02]);
            Assert.Throws<EndOfStreamException>(() => r.Skip(3));
        }

        [Fact]
        public void Seek_ResetsToAbsolutePosition_RelativeToCodestreamStart()
        {
            var r = new CodestreamReader([0xAA, 0xBB, 0xCC, 0xDD]);
            r.ReadByte();
            r.ReadByte();
            r.Seek(0);
            Assert.Equal(0xAA, r.ReadByte());
        }

        [Fact]
        public void Position_ReportsCodestreamRelativeOffset_NotBufferOffset()
        {
            // Codestream starts at offset 4 in a 12-byte host buffer.
            var host = new byte[] { 0, 0, 0, 0, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
            var r = new CodestreamReader(host, 4, 8);
            Assert.Equal(0, r.Position);
            r.ReadByte();
            Assert.Equal(1, r.Position);
        }

        [Fact]
        public void ReadMarker_ValidSocCode_ReturnsIt()
        {
            var r = new CodestreamReader([0xFF, 0x4F]);
            Assert.Equal(MarkerCode.Soc, r.ReadMarker());
        }

        [Fact]
        public void ReadMarker_HighByteNotFF_Throws()
        {
            var r = new CodestreamReader([0xFE, 0x4F]);
            var ex = Assert.Throws<InvalidDataException>(() => r.ReadMarker());
            Assert.Contains("position 0", ex.Message);
        }

        [Fact]
        public void ReadMarker_LowByteBelow4F_Throws()
        {
            // 0xFF00 is the JBIG2 BYTEIN stuff convention, not a J2K marker.
            var r = new CodestreamReader([0xFF, 0x00]);
            Assert.Throws<InvalidDataException>(() => r.ReadMarker());
        }

        [Fact]
        public void ReadSegment_ReturnsSubReaderCoveringPayload_OnlyPayloadBytes()
        {
            // Marker segment with Lxxx=6 followed by 4 payload bytes
            // (Lxxx counts itself, so payload = Lxxx - 2 = 4).
            var data = new byte[]
            {
                0x00, 0x06, // Lxxx = 6
                0xAA, 0xBB, 0xCC, 0xDD, // payload
                0xFF, 0x90, // following marker (SOT) — should not be consumed
            };
            var parent = new CodestreamReader(data);
            var sub = parent.ReadSegment();

            Assert.Equal(4, sub.Length);
            Assert.Equal(0xAA, sub.ReadByte());
            Assert.Equal(0xBB, sub.ReadByte());
            Assert.Equal(0xCC, sub.ReadByte());
            Assert.Equal(0xDD, sub.ReadByte());
            Assert.True(sub.IsAtEnd);
            // Parent cursor has skipped the full Lxxx-length segment.
            Assert.Equal(6, parent.Position);
            Assert.Equal(0xFF90, parent.PeekUInt16BigEndian());
        }

        [Fact]
        public void ReadSegment_LxxxLessThan2_Throws()
        {
            var r = new CodestreamReader([0x00, 0x01]);
            Assert.Throws<InvalidDataException>(() => r.ReadSegment());
        }

        [Fact]
        public void ReadSegment_TruncatedPayload_ThrowsEndOfStream()
        {
            // Claims 10-byte segment but buffer only has 4 payload bytes after Lxxx.
            var data = new byte[] { 0x00, 0x0C, 0x01, 0x02, 0x03, 0x04 };
            var r = new CodestreamReader(data);
            Assert.Throws<EndOfStreamException>(() => r.ReadSegment());
        }
    }
}
