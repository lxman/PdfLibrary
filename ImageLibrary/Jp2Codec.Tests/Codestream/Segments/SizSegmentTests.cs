using Jp2Codec.Codestream;
using Jp2Codec.Codestream.Segments;

namespace Jp2Codec.Tests.Codestream.Segments
{
    public sealed class SizSegmentTests
    {
        private static SizSegment ParseFrom(HeaderBytes h)
        {
            var r = new CodestreamReader(h.ToArray());
            r.ReadMarker(); // consume the SIZ marker (0xFF51)
            return SizSegment.Parse(r.ReadSegment());
        }

        [Fact]
        public void Parse_GrayscaleEightBit_ReadsAllFields()
        {
            SizSegment siz = ParseFrom(new HeaderBytes().Siz(width: 100, height: 60, components: 1));

            Assert.Equal(0, siz.Capabilities);
            Assert.Equal(100u, siz.ReferenceGridWidth);
            Assert.Equal(60u, siz.ReferenceGridHeight);
            Assert.Equal(0u, siz.ImageHorizontalOffset);
            Assert.Equal(0u, siz.ImageVerticalOffset);
            Assert.Equal(100u, siz.TileWidth);
            Assert.Equal(60u, siz.TileHeight);
            Assert.Equal(1, siz.NumberOfComponents);
            Assert.Equal(100, siz.ImageWidth);
            Assert.Equal(60, siz.ImageHeight);

            Assert.Equal(8, siz.Components[0].BitDepth);
            Assert.False(siz.Components[0].IsSigned);
            Assert.Equal(1, siz.Components[0].HorizontalSubsampling);
            Assert.Equal(1, siz.Components[0].VerticalSubsampling);
            Assert.Equal(100, siz.ComponentWidth(0));
            Assert.Equal(60, siz.ComponentHeight(0));
        }

        [Fact]
        public void Parse_ThreeComponentRgb_ReportsThreeComponents()
        {
            SizSegment siz = ParseFrom(new HeaderBytes().Siz(width: 16, height: 8, components: 3));
            Assert.Equal(3, siz.NumberOfComponents);
            for (var c = 0; c < 3; c++)
            {
                Assert.Equal(8, siz.Components[c].BitDepth);
                Assert.Equal(16, siz.ComponentWidth(c));
                Assert.Equal(8, siz.ComponentHeight(c));
            }
        }

        [Fact]
        public void Parse_SignedComponent_SetsSignedFlag()
        {
            SizSegment siz = ParseFrom(new HeaderBytes().Siz(width: 10, height: 10, components: 1, bitDepth: 12, isSigned: true));
            Assert.True(siz.Components[0].IsSigned);
            Assert.Equal(12, siz.Components[0].BitDepth);
        }

        [Fact]
        public void ComponentWidth_AppliesSubsampling_WithCeilingDivision()
        {
            // 4:2:0-style subsampling: chroma is half-resolution.
            // Grid 100x60 with XRsiz=2, YRsiz=2 → component 50x30.
            SizSegment siz = ParseFrom(new HeaderBytes().Siz(
                width: 100, height: 60, components: 1, xrSiz: 2, yrSiz: 2));
            Assert.Equal(50, siz.ComponentWidth(0));
            Assert.Equal(30, siz.ComponentHeight(0));
        }

        [Fact]
        public void ComponentWidth_OddGridWithSubsampling_RoundsUp()
        {
            // Grid 101x61 with XRsiz=2 → ceil(101/2) = 51.
            SizSegment siz = ParseFrom(new HeaderBytes().Siz(
                width: 101, height: 61, components: 1, xrSiz: 2, yrSiz: 2));
            Assert.Equal(51, siz.ComponentWidth(0));
            Assert.Equal(31, siz.ComponentHeight(0));
        }

        [Fact]
        public void Parse_CsizZero_Throws()
        {
            var r = new CodestreamReader(new HeaderBytes()
                .Marker(0xFF51).U16(38)
                .U16(0)
                .U32(10).U32(10).U32(0).U32(0)
                .U32(10).U32(10).U32(0).U32(0)
                .U16(0)        // Csiz = 0 — invalid
                .ToArray());
            r.ReadMarker();
            Assert.Throws<InvalidDataException>(() => SizSegment.Parse(r.ReadSegment()));
        }

        [Fact]
        public void Parse_ZeroSubsampling_Throws()
        {
            var r = new CodestreamReader(new HeaderBytes()
                .Marker(0xFF51).U16(41)
                .U16(0)
                .U32(10).U32(10).U32(0).U32(0)
                .U32(10).U32(10).U32(0).U32(0)
                .U16(1)
                .U8(7)         // Ssiz = 7 → bit depth 8
                .U8(0)         // XRsiz = 0 — invalid
                .U8(1)
                .ToArray());
            r.ReadMarker();
            Assert.Throws<InvalidDataException>(() => SizSegment.Parse(r.ReadSegment()));
        }

        [Fact]
        public void Parse_ImageOffsetGreaterThanOrEqualToSize_Throws()
        {
            var r = new CodestreamReader(new HeaderBytes()
                .Marker(0xFF51).U16(41)
                .U16(0)
                .U32(10).U32(10)
                .U32(10).U32(0)   // XOsiz == Xsiz — degenerate
                .U32(10).U32(10).U32(0).U32(0)
                .U16(1).U8(7).U8(1).U8(1)
                .ToArray());
            r.ReadMarker();
            Assert.Throws<InvalidDataException>(() => SizSegment.Parse(r.ReadSegment()));
        }
    }
}
