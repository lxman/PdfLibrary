using Jp2Codec.Codestream;
using Jp2Codec.Codestream.Segments;
using Jp2Codec.Tests.Codestream;

namespace Jp2Codec.Tests.Codestream.Segments
{
    public sealed class ComSegmentTests
    {
        private static ComSegment ParseFrom(HeaderBytes h)
        {
            var r = new CodestreamReader(h.ToArray());
            r.ReadMarker();
            return ComSegment.Parse(r.ReadSegment());
        }

        [Fact]
        public void Parse_LatinTextComment_ReadsAsString()
        {
            ComSegment com = ParseFrom(new HeaderBytes().ComText("OpenJPEG"));
            Assert.Equal(CommentRegistration.Latin9, com.Registration);
            Assert.Equal("OpenJPEG", com.GetTextOrEmpty());
        }

        [Fact]
        public void Parse_BinaryComment_TextAccessorIsEmpty()
        {
            var h = new HeaderBytes();
            int at = h.BeginSegment(0xFF64);
            h.U16(0);                        // Rcom = Binary
            h.U8(0xDE).U8(0xAD).U8(0xBE).U8(0xEF);
            h.EndSegment(at);
            ComSegment com = ParseFrom(h);
            Assert.Equal(CommentRegistration.Binary, com.Registration);
            Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, com.Data);
            Assert.Equal(string.Empty, com.GetTextOrEmpty());
        }
    }
}
