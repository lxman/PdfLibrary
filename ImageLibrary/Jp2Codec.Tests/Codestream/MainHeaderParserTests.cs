using System.IO;
using Jp2Codec.Codestream;
using Jp2Codec.Codestream.Segments;

namespace Jp2Codec.Tests.Codestream
{
    public sealed class MainHeaderParserTests
    {
        private static byte[] BuildMinimalHeaderEndingAtSot()
        {
            return new HeaderBytes()
                .Marker(0xFF4F)                                          // SOC (no length)
                .Siz(width: 64, height: 64, components: 3)
                .Cod(decompositionLevels: 3)
                .QcdReversible(decompositionLevels: 3)
                .Marker(0xFF90).U16(10).U16(0).U32(20).U8(0).U8(1)       // SOT (will not be consumed by main-header parser)
                .ToArray();
        }

        [Fact]
        public void Parse_MinimalHeader_StopsAtSot()
        {
            var r = new CodestreamReader(BuildMinimalHeaderEndingAtSot());
            MainHeader header = MainHeaderParser.Parse(r);

            Assert.NotNull(header.Siz);
            Assert.Equal(3, header.Siz.NumberOfComponents);
            Assert.Equal(64, header.Siz.ImageWidth);

            Assert.NotNull(header.Cod);
            Assert.Equal(3, header.Cod.DecompositionLevels);

            Assert.NotNull(header.Qcd);
            Assert.Equal(QuantizationStyle.None, header.Qcd.Style);

            Assert.Empty(header.CocOverrides);
            Assert.Empty(header.QccOverrides);
            Assert.Empty(header.Comments);

            // EndPosition points at the SOT that we stopped on.
            Assert.Equal(header.EndPosition, r.Position);
            Assert.Equal(MarkerCode.Sot, r.PeekUInt16BigEndian());
        }

        [Fact]
        public void Parse_WithCommentBetweenQcdAndSot_RecordsComment()
        {
            byte[] data = new HeaderBytes()
                .Marker(0xFF4F)
                .Siz(width: 64, height: 64, components: 1)
                .Cod(decompositionLevels: 3)
                .QcdReversible(decompositionLevels: 3)
                .ComText("ImageMagick")
                .Marker(0xFF90).U16(10).U16(0).U32(20).U8(0).U8(1)
                .ToArray();
            MainHeader header = MainHeaderParser.Parse(new CodestreamReader(data));
            Assert.Single(header.Comments);
            Assert.Equal("ImageMagick", header.Comments[0].GetTextOrEmpty());
        }

        [Fact]
        public void Parse_DuplicateCod_Throws()
        {
            byte[] data = new HeaderBytes()
                .Marker(0xFF4F)
                .Siz(width: 32, height: 32, components: 1)
                .Cod()
                .Cod()
                .QcdReversible()
                .Marker(0xFF90).U16(10).U16(0).U32(20).U8(0).U8(1)
                .ToArray();
            Assert.Throws<InvalidDataException>(() =>
                MainHeaderParser.Parse(new CodestreamReader(data)));
        }

        [Fact]
        public void Parse_MissingCod_Throws()
        {
            byte[] data = new HeaderBytes()
                .Marker(0xFF4F)
                .Siz(width: 32, height: 32, components: 1)
                .QcdReversible()
                .Marker(0xFF90).U16(10).U16(0).U32(20).U8(0).U8(1)
                .ToArray();
            Assert.Throws<InvalidDataException>(() =>
                MainHeaderParser.Parse(new CodestreamReader(data)));
        }

        [Fact]
        public void Parse_MissingQcd_Throws()
        {
            byte[] data = new HeaderBytes()
                .Marker(0xFF4F)
                .Siz(width: 32, height: 32, components: 1)
                .Cod()
                .Marker(0xFF90).U16(10).U16(0).U32(20).U8(0).U8(1)
                .ToArray();
            Assert.Throws<InvalidDataException>(() =>
                MainHeaderParser.Parse(new CodestreamReader(data)));
        }

        [Fact]
        public void Parse_FirstMarkerNotSoc_Throws()
        {
            byte[] data = new HeaderBytes()
                .Siz(width: 32, height: 32, components: 1)  // no SOC up front
                .ToArray();
            Assert.Throws<InvalidDataException>(() =>
                MainHeaderParser.Parse(new CodestreamReader(data)));
        }

        [Fact]
        public void Parse_SizMissingAfterSoc_Throws()
        {
            byte[] data = new HeaderBytes()
                .Marker(0xFF4F)
                .Cod()  // COD without preceding SIZ
                .ToArray();
            Assert.Throws<InvalidDataException>(() =>
                MainHeaderParser.Parse(new CodestreamReader(data)));
        }
    }
}
