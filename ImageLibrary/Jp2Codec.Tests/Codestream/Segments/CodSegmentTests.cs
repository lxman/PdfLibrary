using Jp2Codec.Codestream;
using Jp2Codec.Codestream.Segments;

namespace Jp2Codec.Tests.Codestream.Segments
{
    public sealed class CodSegmentTests
    {
        private static CodSegment ParseFrom(HeaderBytes h)
        {
            var r = new CodestreamReader(h.ToArray());
            r.ReadMarker();
            return CodSegment.Parse(r.ReadSegment());
        }

        [Fact]
        public void Parse_DefaultsReversible5x3_ReadsCorePcods()
        {
            CodSegment cod = ParseFrom(new HeaderBytes().Cod(
                decompositionLevels: 5, xcbExp: 4, ycbExp: 4,
                progressionOrder: 0, layers: 1,
                reversibleTransform: true));

            Assert.False(cod.UseExplicitPrecincts);
            Assert.False(cod.UseSopMarkers);
            Assert.False(cod.UseEphMarkers);
            Assert.Equal(ProgressionOrder.Lrcp, cod.ProgressionOrder);
            Assert.Equal(1, cod.NumberOfLayers);
            Assert.False(cod.UseMultipleComponentTransform);
            Assert.Equal(5, cod.DecompositionLevels);
            Assert.Equal(4, cod.CodeBlockWidthExponent);
            Assert.Equal(4, cod.CodeBlockHeightExponent);
            Assert.Equal(CodeBlockStyle.None, cod.CodeBlockStyle);
            Assert.Equal(WaveletTransform.Reversible5x3, cod.WaveletTransform);
        }

        [Fact]
        public void Parse_Irreversible9x7WithMct_FlagsSet()
        {
            CodSegment cod = ParseFrom(new HeaderBytes().Cod(
                progressionOrder: 0, layers: 8,
                mct: true, reversibleTransform: false));
            Assert.True(cod.UseMultipleComponentTransform);
            Assert.Equal(WaveletTransform.Irreversible9x7, cod.WaveletTransform);
            Assert.Equal(8, cod.NumberOfLayers);
        }

        [Fact]
        public void Parse_DefaultPrecincts_All32k()
        {
            CodSegment cod = ParseFrom(new HeaderBytes().Cod(decompositionLevels: 3));
            Assert.Equal(4, cod.PrecinctWidthExponents.Length);
            foreach (int exp in cod.PrecinctWidthExponents) Assert.Equal(15, exp);
            foreach (int exp in cod.PrecinctHeightExponents) Assert.Equal(15, exp);
        }

        [Fact]
        public void Parse_ExplicitPrecincts_PerResolutionPpxPpyDecoded()
        {
            // Hand-build a COD with explicit precincts: 3 decomposition levels
            // → 4 resolution levels → 4 PP bytes. Pack PPx, PPy in low/high
            // nibbles per Table A-21.
            var h = new HeaderBytes();
            int at = h.BeginSegment(0xFF52);
            h.U8(0x01);   // Scod: explicit precincts
            h.U8(0);      // progression: LRCP
            h.U16(1);     // layers
            h.U8(0);      // no MCT
            h.U8(3);      // decomposition levels
            h.U8(2); h.U8(2);  // xcb-2, ycb-2 → exponents 4, 4
            h.U8(0);      // code-block style
            h.U8(1);      // reversible 5/3
            // 4 PP bytes: PPx = {3, 4, 5, 6}, PPy = {7, 6, 5, 4}
            h.U8((7 << 4) | 3);
            h.U8((6 << 4) | 4);
            h.U8((5 << 4) | 5);
            h.U8((4 << 4) | 6);
            h.EndSegment(at);

            CodSegment cod = ParseFrom(h);
            Assert.True(cod.UseExplicitPrecincts);
            Assert.Equal(new[] { 3, 4, 5, 6 }, cod.PrecinctWidthExponents);
            Assert.Equal(new[] { 7, 6, 5, 4 }, cod.PrecinctHeightExponents);
        }

        [Theory]
        [InlineData(5)]   // beyond progression-order range [0..4]
        [InlineData(99)]
        public void Parse_BadProgressionOrder_Throws(int prog)
        {
            var h = new HeaderBytes();
            int at = h.BeginSegment(0xFF52);
            h.U8(0).U8(prog).U16(1).U8(0).U8(3).U8(2).U8(2).U8(0).U8(1);
            h.EndSegment(at);
            Assert.Throws<InvalidDataException>(() => ParseFrom(h));
        }

        [Fact]
        public void Parse_CodeBlockAreaExceedsCap_Throws()
        {
            // xcb-2 = 7 (xcb = 9), ycb-2 = 7 (ycb = 9) → sum = 18 > 12.
            var h = new HeaderBytes();
            int at = h.BeginSegment(0xFF52);
            h.U8(0).U8(0).U16(1).U8(0).U8(3).U8(7).U8(7).U8(0).U8(1);
            h.EndSegment(at);
            Assert.Throws<InvalidDataException>(() => ParseFrom(h));
        }

        [Fact]
        public void Parse_CodeBlockStyleFlags_ParsedAsFlagsEnum()
        {
            var h = new HeaderBytes();
            int at = h.BeginSegment(0xFF52);
            h.U8(0).U8(0).U16(1).U8(0).U8(3).U8(2).U8(2);
            h.U8(0x21); // SegmentationSymbols (0x20) | SelectiveBypass (0x01)
            h.U8(1);
            h.EndSegment(at);

            CodSegment cod = ParseFrom(h);
            Assert.True(cod.CodeBlockStyle.HasFlag(CodeBlockStyle.SegmentationSymbols));
            Assert.True(cod.CodeBlockStyle.HasFlag(CodeBlockStyle.SelectiveBypass));
            Assert.False(cod.CodeBlockStyle.HasFlag(CodeBlockStyle.TerminationOnPass));
        }
    }
}
