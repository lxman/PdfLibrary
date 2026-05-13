using System;
using Jp2Codec.Tier1;

namespace Jp2Codec.Tests.Tier1
{
    public sealed class Tier1StateTests
    {
        [Fact]
        public void Construction_StoresWidthAndHeight()
        {
            var s = new Tier1State(7, 5);
            Assert.Equal(7, s.Width);
            Assert.Equal(5, s.Height);
        }

        [Theory]
        [InlineData(1, 1, 4, 1)]
        [InlineData(8, 4, 4, 1)]
        [InlineData(8, 5, 8, 2)]
        [InlineData(8, 8, 8, 2)]
        [InlineData(64, 64, 64, 16)]
        [InlineData(3, 13, 16, 4)]
        public void PaddedHeight_RoundsUpToMultipleOfFour_AndStripeCountFollows(
            int width, int height, int expectedPaddedHeight, int expectedStripes)
        {
            var s = new Tier1State(width, height);
            Assert.Equal(expectedPaddedHeight, s.PaddedHeight);
            Assert.Equal(expectedStripes, s.StripeCount);
        }

        [Theory]
        [InlineData(0, 4)]
        [InlineData(-1, 4)]
        [InlineData(4, 0)]
        [InlineData(4, -3)]
        public void Construction_RejectsNonPositiveDimensions(int w, int h)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Tier1State(w, h));
        }

        [Fact]
        public void NewState_AllFlagsAndMagnitudesAreZero()
        {
            var s = new Tier1State(8, 8);
            for (var y = 0; y < s.Height; y++)
            for (var x = 0; x < s.Width; x++)
            {
                Assert.Equal(0, s.GetFlags(x, y));
                Assert.Equal(0, s.GetMagnitude(x, y));
            }
        }

        [Fact]
        public void SetFlag_OnlyTouchesRequestedBits()
        {
            var s = new Tier1State(4, 4);
            s.SetFlag(2, 1, Tier1State.SignificanceFlag);
            s.SetFlag(2, 1, Tier1State.RefinedFlag);

            byte expected = (byte)(Tier1State.SignificanceFlag | Tier1State.RefinedFlag);
            Assert.Equal(expected, s.GetFlags(2, 1));
            Assert.True(s.HasFlag(2, 1, Tier1State.SignificanceFlag));
            Assert.True(s.HasFlag(2, 1, Tier1State.RefinedFlag));
            Assert.False(s.HasFlag(2, 1, Tier1State.SignFlag));
            Assert.False(s.HasFlag(2, 1, Tier1State.VisitedFlag));
        }

        [Fact]
        public void SetFlag_IsIdempotent()
        {
            var s = new Tier1State(4, 4);
            s.SetFlag(0, 0, Tier1State.SignificanceFlag);
            s.SetFlag(0, 0, Tier1State.SignificanceFlag);
            Assert.Equal(Tier1State.SignificanceFlag, s.GetFlags(0, 0));
        }

        [Fact]
        public void Magnitude_RoundTrips()
        {
            var s = new Tier1State(4, 4);
            s.SetMagnitude(1, 2, 0x1234);
            Assert.Equal(0x1234, s.GetMagnitude(1, 2));
            // Untouched coefficients remain zero.
            Assert.Equal(0, s.GetMagnitude(0, 0));
            Assert.Equal(0, s.GetMagnitude(3, 3));
        }

        [Fact]
        public void ResetVisited_ClearsOnlyVisitedBit()
        {
            var s = new Tier1State(4, 4);
            byte allFour = (byte)(
                Tier1State.SignificanceFlag |
                Tier1State.SignFlag |
                Tier1State.VisitedFlag |
                Tier1State.RefinedFlag);

            for (var y = 0; y < s.Height; y++)
            for (var x = 0; x < s.Width; x++)
                s.SetFlag(x, y, allFour);

            s.ResetVisited();

            byte expected = (byte)(allFour & ~Tier1State.VisitedFlag);
            for (var y = 0; y < s.Height; y++)
            for (var x = 0; x < s.Width; x++)
                Assert.Equal(expected, s.GetFlags(x, y));
        }

        [Fact]
        public void GetSignificanceNeighbourhood_OffGridNeighbours_ReadAsInsignificant()
        {
            // Corner coefficient (0, 0) — five of its 8 neighbours are off-grid.
            var s = new Tier1State(4, 4);
            // Mark every interior coefficient as significant; corner still sees zeroes
            // for the off-grid spots.
            for (var y = 0; y < s.Height; y++)
            for (var x = 0; x < s.Width; x++)
                s.SetFlag(x, y, Tier1State.SignificanceFlag);

            // Temporarily clear (0,0) so we read its neighbourhood without seeing itself.
            // (Itself isn't part of the 8-neighbour pattern anyway.)
            byte n = s.GetSignificanceNeighbourhood(0, 0);
            // Expected: NW/N/NE/W/SW = off-grid (0); E/S/SE = significant (1).
            // bit 4 = E, bit 6 = S, bit 7 = SE → 0x10 | 0x40 | 0x80 = 0xD0
            Assert.Equal((byte)0xD0, n);
        }

        [Fact]
        public void GetSignificanceNeighbourhood_InteriorCoefficient_ReturnsAllEight()
        {
            var s = new Tier1State(5, 5);
            // Set every neighbour of (2,2) significant; (2,2) itself stays clear.
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                s.SetFlag(2 + dx, 2 + dy, Tier1State.SignificanceFlag);
            }
            Assert.Equal((byte)0xFF, s.GetSignificanceNeighbourhood(2, 2));
        }

        [Fact]
        public void CountSignificantNeighbours_MatchesNeighbourhoodPopulationCount()
        {
            var s = new Tier1State(5, 5);
            s.SetFlag(1, 1, Tier1State.SignificanceFlag);
            s.SetFlag(3, 1, Tier1State.SignificanceFlag);
            s.SetFlag(1, 3, Tier1State.SignificanceFlag);
            s.SetFlag(3, 3, Tier1State.SignificanceFlag);
            // (2,2) sees four diagonal neighbours significant.
            Assert.Equal(4, s.CountSignificantNeighbours(2, 2));
        }

        [Fact]
        public void GetSignContribution_HonoursSignAndSignificance()
        {
            var s = new Tier1State(4, 4);
            // Put a positive-significant neighbour to the north of (1,1) and a
            // negative-significant neighbour to the south. East stays insignificant.
            s.SetFlag(1, 0, Tier1State.SignificanceFlag); // north, sign-bit clear → +1
            s.SetFlag(1, 2, (byte)(Tier1State.SignificanceFlag | Tier1State.SignFlag)); // south → -1
            // West gets a sign bit but no significance — should still report 0.
            s.SetFlag(0, 1, Tier1State.SignFlag);

            Assert.Equal(+1, s.GetSignContribution(1, 1, NeighbourDirection.North));
            Assert.Equal(-1, s.GetSignContribution(1, 1, NeighbourDirection.South));
            Assert.Equal( 0, s.GetSignContribution(1, 1, NeighbourDirection.West));
            Assert.Equal( 0, s.GetSignContribution(1, 1, NeighbourDirection.East));
        }

        [Fact]
        public void PaddedRowsBeyondHeight_AreReadableAndDefaultZero()
        {
            // Codeblock height 5 → padded height 8 → rows 5..7 are stripe-padding.
            var s = new Tier1State(4, 5);
            Assert.Equal(8, s.PaddedHeight);
            for (var y = s.Height; y < s.PaddedHeight; y++)
            for (var x = 0; x < s.Width; x++)
            {
                Assert.Equal(0, s.GetFlags(x, y));
                // We can also write to the padding rows without it leaking elsewhere.
                s.SetFlag(x, y, Tier1State.SignificanceFlag);
            }
            // Setting padding-row flags doesn't touch any in-range coefficient.
            for (var y = 0; y < s.Height; y++)
            for (var x = 0; x < s.Width; x++)
                Assert.Equal(0, s.GetFlags(x, y));
        }
    }
}
