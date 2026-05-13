using Jp2Codec.Geometry;

namespace Jp2Codec.Tests.Geometry;

public class CoordMathTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(0, 3, 0)]
    [InlineData(1, 1, 1)]
    [InlineData(2, 1, 1)]
    [InlineData(3, 1, 2)]
    [InlineData(4, 1, 2)]
    [InlineData(5, 1, 3)]
    [InlineData(7, 3, 1)]
    [InlineData(8, 3, 1)]
    [InlineData(9, 3, 2)]
    [InlineData(16, 4, 1)]
    public void CeilDivPow2_NonNegative_MatchesSpec(int value, int exponent, int expected)
    {
        Assert.Equal(expected, CoordMath.CeilDivPow2(value, exponent));
    }

    [Theory]
    // For the HL/LH/HH subbands the encoder applies value = tcx0 - 2^(n_b-1),
    // which can go negative. ceil(-1/2) = 0; ceil(-2/2) = -1; ceil(-3/2) = -1;
    // ceil(-3/4) = 0; ceil(-4/4) = -1.
    [InlineData(-1, 1, 0)]
    [InlineData(-2, 1, -1)]
    [InlineData(-3, 1, -1)]
    [InlineData(-3, 2, 0)]
    [InlineData(-4, 2, -1)]
    [InlineData(-5, 2, -1)]
    [InlineData(-8, 2, -2)]
    public void CeilDivPow2_Negative_MatchesSpec(int value, int exponent, int expected)
    {
        Assert.Equal(expected, CoordMath.CeilDivPow2(value, exponent));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(5, 0, 5)]
    [InlineData(7, 1, 3)]
    [InlineData(8, 1, 4)]
    [InlineData(9, 2, 2)]
    [InlineData(-1, 1, -1)]
    [InlineData(-2, 1, -1)]
    [InlineData(-3, 1, -2)]
    public void FloorDivPow2_MatchesSpec(int value, int exponent, int expected)
    {
        Assert.Equal(expected, CoordMath.FloorDivPow2(value, exponent));
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(7, 3, 3)]
    [InlineData(9, 3, 3)]
    [InlineData(10, 3, 4)]
    public void CeilDiv_Generic_MatchesSpec(int num, int den, int expected)
    {
        Assert.Equal(expected, CoordMath.CeilDiv(num, den));
    }
}
