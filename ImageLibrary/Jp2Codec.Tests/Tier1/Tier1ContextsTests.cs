using Jp2Codec.Mq;
using Jp2Codec.Tier1;

namespace Jp2Codec.Tests.Tier1
{
    public sealed class Tier1ContextsTests
    {
        // Neighbour-bit constants — must match Tier1State.GetSignificanceNeighbourhood.
        private const byte NW = 0x01;
        private const byte N  = 0x02;
        private const byte NE = 0x04;
        private const byte W  = 0x08;
        private const byte E  = 0x10;
        private const byte SW = 0x20;
        private const byte S  = 0x40;
        private const byte SE = 0x80;

        private static byte Pat(params byte[] dirs)
        {
            byte p = 0;
            foreach (byte d in dirs) p |= d;
            return p;
        }

        // ==== Zero-coding ===================================================

        // Context indices in the spec are written as offsets 0..8 inside the
        // ZC group; absolute MQ context indices add Jp2MqContextSet.ZeroCoding (= 0).

        [Fact] public void Zc_LL_NoNeighbours_IsContextZero() =>
            Assert.Equal(Jp2MqContextSet.ZeroCoding + 0,
                Tier1Contexts.ZeroCoding(SubbandOrientation.LL, Pat()));

        [Fact] public void Zc_LL_OneDiagonal_IsContextOne() =>
            Assert.Equal(Jp2MqContextSet.ZeroCoding + 1,
                Tier1Contexts.ZeroCoding(SubbandOrientation.LL, Pat(NW)));

        [Fact] public void Zc_LL_TwoDiagonals_IsContextTwo() =>
            Assert.Equal(Jp2MqContextSet.ZeroCoding + 2,
                Tier1Contexts.ZeroCoding(SubbandOrientation.LL, Pat(NW, NE)));

        [Fact] public void Zc_LL_ThreeDiagonals_StaysAtContextTwo() =>
            Assert.Equal(Jp2MqContextSet.ZeroCoding + 2,
                Tier1Contexts.ZeroCoding(SubbandOrientation.LL, Pat(NW, NE, SW)));

        [Fact] public void Zc_LL_OneVertical_IsContextThree() =>
            Assert.Equal(Jp2MqContextSet.ZeroCoding + 3,
                Tier1Contexts.ZeroCoding(SubbandOrientation.LL, Pat(N)));

        [Fact] public void Zc_LL_TwoVertical_IsContextFour() =>
            Assert.Equal(Jp2MqContextSet.ZeroCoding + 4,
                Tier1Contexts.ZeroCoding(SubbandOrientation.LL, Pat(N, S)));

        [Fact] public void Zc_LL_OneHorizontal_NoDiagonals_IsContextFive() =>
            Assert.Equal(Jp2MqContextSet.ZeroCoding + 5,
                Tier1Contexts.ZeroCoding(SubbandOrientation.LL, Pat(W)));

        [Fact] public void Zc_LL_OneHorizontal_PlusDiagonal_IsContextSix() =>
            Assert.Equal(Jp2MqContextSet.ZeroCoding + 6,
                Tier1Contexts.ZeroCoding(SubbandOrientation.LL, Pat(W, NW)));

        [Fact] public void Zc_LL_OneHorizontal_PlusVertical_IsContextSeven() =>
            Assert.Equal(Jp2MqContextSet.ZeroCoding + 7,
                Tier1Contexts.ZeroCoding(SubbandOrientation.LL, Pat(W, N)));

        [Fact] public void Zc_LL_TwoHorizontal_IsContextEight() =>
            Assert.Equal(Jp2MqContextSet.ZeroCoding + 8,
                Tier1Contexts.ZeroCoding(SubbandOrientation.LL, Pat(W, E)));

        [Fact] public void Zc_LH_SharesTableD1WithLL()
        {
            // Spot-check a few patterns that disagree between D-1 and D-2.
            byte hCol = Pat(W);   // LL/LH → 5;  HL → 3
            byte vCol = Pat(N);   // LL/LH → 3;  HL → 5
            Assert.Equal(
                Tier1Contexts.ZeroCoding(SubbandOrientation.LL, hCol),
                Tier1Contexts.ZeroCoding(SubbandOrientation.LH, hCol));
            Assert.Equal(
                Tier1Contexts.ZeroCoding(SubbandOrientation.LL, vCol),
                Tier1Contexts.ZeroCoding(SubbandOrientation.LH, vCol));
        }

        [Fact] public void Zc_HL_SwapsHorizontalAndVerticalCompared_ToLL()
        {
            // In Table D-2 the H/V roles are swapped:
            //   one H neighbour → ctx 3   (was 5 in LL)
            //   one V neighbour → ctx 5   (was 3 in LL)
            //   two H neighbours → ctx 4
            //   two V neighbours → ctx 8
            Assert.Equal(Jp2MqContextSet.ZeroCoding + 3,
                Tier1Contexts.ZeroCoding(SubbandOrientation.HL, Pat(W)));
            Assert.Equal(Jp2MqContextSet.ZeroCoding + 5,
                Tier1Contexts.ZeroCoding(SubbandOrientation.HL, Pat(N)));
            Assert.Equal(Jp2MqContextSet.ZeroCoding + 4,
                Tier1Contexts.ZeroCoding(SubbandOrientation.HL, Pat(W, E)));
            Assert.Equal(Jp2MqContextSet.ZeroCoding + 8,
                Tier1Contexts.ZeroCoding(SubbandOrientation.HL, Pat(N, S)));
        }

        [Fact] public void Zc_HH_NoNeighbours_IsContextZero() =>
            Assert.Equal(Jp2MqContextSet.ZeroCoding + 0,
                Tier1Contexts.ZeroCoding(SubbandOrientation.HH, Pat()));

        [Fact] public void Zc_HH_OneHvNeighbour_NoDiag_IsContextOne() =>
            Assert.Equal(Jp2MqContextSet.ZeroCoding + 1,
                Tier1Contexts.ZeroCoding(SubbandOrientation.HH, Pat(W)));

        [Fact] public void Zc_HH_TwoHvNeighbours_NoDiag_IsContextTwo() =>
            Assert.Equal(Jp2MqContextSet.ZeroCoding + 2,
                Tier1Contexts.ZeroCoding(SubbandOrientation.HH, Pat(W, E)));

        [Fact] public void Zc_HH_OneDiagonal_NoHv_IsContextThree() =>
            Assert.Equal(Jp2MqContextSet.ZeroCoding + 3,
                Tier1Contexts.ZeroCoding(SubbandOrientation.HH, Pat(NW)));

        [Fact] public void Zc_HH_OneDiagonal_OneHv_IsContextFour() =>
            Assert.Equal(Jp2MqContextSet.ZeroCoding + 4,
                Tier1Contexts.ZeroCoding(SubbandOrientation.HH, Pat(NW, W)));

        [Fact] public void Zc_HH_OneDiagonal_TwoHv_IsContextFive() =>
            Assert.Equal(Jp2MqContextSet.ZeroCoding + 5,
                Tier1Contexts.ZeroCoding(SubbandOrientation.HH, Pat(NW, W, E)));

        [Fact] public void Zc_HH_TwoDiagonals_NoHv_IsContextSix() =>
            Assert.Equal(Jp2MqContextSet.ZeroCoding + 6,
                Tier1Contexts.ZeroCoding(SubbandOrientation.HH, Pat(NW, NE)));

        [Fact] public void Zc_HH_TwoDiagonals_OneHv_IsContextSeven() =>
            Assert.Equal(Jp2MqContextSet.ZeroCoding + 7,
                Tier1Contexts.ZeroCoding(SubbandOrientation.HH, Pat(NW, NE, W)));

        [Fact] public void Zc_HH_ThreeOrMoreDiagonals_IsContextEight() =>
            Assert.Equal(Jp2MqContextSet.ZeroCoding + 8,
                Tier1Contexts.ZeroCoding(SubbandOrientation.HH, Pat(NW, NE, SW)));

        // ==== Sign-coding ===================================================

        [Theory]
        [InlineData( 1,  1, 4, 0)]
        [InlineData( 1,  0, 3, 0)]
        [InlineData( 1, -1, 2, 0)]
        [InlineData( 0,  1, 1, 0)]
        [InlineData( 0,  0, 0, 0)]
        [InlineData( 0, -1, 1, 1)]
        [InlineData(-1,  1, 2, 1)]
        [InlineData(-1,  0, 3, 1)]
        [InlineData(-1, -1, 4, 1)]
        public void Sc_TableD4_AllNineCombinations(int h, int v, int expectedOffset, int expectedXor)
        {
            (int ctx, int xor) = Tier1Contexts.SignCoding(h, v);
            Assert.Equal(Jp2MqContextSet.SignCoding + expectedOffset, ctx);
            Assert.Equal(expectedXor, xor);
        }

        [Theory]
        [InlineData( 2,  0)]
        [InlineData(-2,  0)]
        [InlineData( 0,  2)]
        [InlineData( 0, -2)]
        public void Sc_RejectsContributionsOutsideMinusOneToPlusOne(int h, int v)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Tier1Contexts.SignCoding(h, v));
        }

        // ==== Magnitude refinement ==========================================

        [Fact] public void Mr_NotYetRefined_NoSignificantNeighbours_IsContext14() =>
            Assert.Equal(Jp2MqContextSet.MagnitudeRefinement + 0,
                Tier1Contexts.MagnitudeRefinement(alreadyRefined: false, significantNeighbourCount: 0));

        [Fact] public void Mr_NotYetRefined_AnySignificantNeighbour_IsContext15() =>
            Assert.Equal(Jp2MqContextSet.MagnitudeRefinement + 1,
                Tier1Contexts.MagnitudeRefinement(alreadyRefined: false, significantNeighbourCount: 1));

        [Fact] public void Mr_NotYetRefined_ManyNeighbours_StaysAtContext15() =>
            Assert.Equal(Jp2MqContextSet.MagnitudeRefinement + 1,
                Tier1Contexts.MagnitudeRefinement(alreadyRefined: false, significantNeighbourCount: 8));

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(8)]
        public void Mr_AlreadyRefined_AlwaysContext16(int neighbourCount) =>
            Assert.Equal(Jp2MqContextSet.MagnitudeRefinement + 2,
                Tier1Contexts.MagnitudeRefinement(alreadyRefined: true, significantNeighbourCount: neighbourCount));

        [Fact] public void Mr_RejectsNegativeNeighbourCount() =>
            Assert.Throws<ArgumentOutOfRangeException>(
                () => Tier1Contexts.MagnitudeRefinement(false, -1));

        // ==== End-to-end pairing with Tier1State.GetSignificanceNeighbourhood ====

        [Fact]
        public void IntegratedWithStateGrid_ZcLookupReadsTheRightNeighbourhood()
        {
            // Place a single significant neighbour to the west of (2, 2). In an LL
            // subband, one horizontal neighbour with no diagonals → ctx offset 5.
            var s = new Tier1State(8, 8);
            s.SetFlag(1, 2, Tier1State.SignificanceFlag);
            byte n = s.GetSignificanceNeighbourhood(2, 2);
            Assert.Equal(Jp2MqContextSet.ZeroCoding + 5,
                Tier1Contexts.ZeroCoding(SubbandOrientation.LL, n));
        }

        [Fact]
        public void IntegratedWithStateGrid_SignContributionsClampCorrectly()
        {
            // Two significant negative neighbours both to the west and east →
            // both contribute -1; clamped sum = -1 (per spec, after clamp).
            // Caller is responsible for the clamp; we just verify GetSignContribution
            // pairs with our table.
            var s = new Tier1State(8, 8);
            s.SetFlag(1, 2, (byte)(Tier1State.SignificanceFlag | Tier1State.SignFlag));
            s.SetFlag(3, 2, (byte)(Tier1State.SignificanceFlag | Tier1State.SignFlag));
            int wContrib = s.GetSignContribution(2, 2, NeighbourDirection.West);
            int eContrib = s.GetSignContribution(2, 2, NeighbourDirection.East);
            int hSum = wContrib + eContrib;
            int hClamped = Math.Sign(hSum); // -1, 0, or +1
            Assert.Equal(-1, hClamped);

            (int ctx, int xor) = Tier1Contexts.SignCoding(hClamped, 0);
            // (h=-1, v=0) → offset 3, xor 1
            Assert.Equal(Jp2MqContextSet.SignCoding + 3, ctx);
            Assert.Equal(1, xor);
        }
    }
}
