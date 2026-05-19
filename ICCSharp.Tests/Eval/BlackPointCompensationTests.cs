using ICCSharp.Eval;
using ICCSharp.IO;

namespace ICCSharp.Tests.Eval;

public class BlackPointCompensationTests
{
    [Fact]
    public void Source_black_maps_to_dest_black_exactly()
    {
        XyzNumber srcBlack = new(0.005, 0.005, 0.005);
        XyzNumber dstBlack = new(0.001, 0.001, 0.001);
        MatrixTransform bpc = BlackPointCompensation.Build(srcBlack, dstBlack);

        (double X, double Y, double Z) = bpc.Transform(srcBlack.X, srcBlack.Y, srcBlack.Z);
        Assert.Equal(dstBlack.X, X, 12);
        Assert.Equal(dstBlack.Y, Y, 12);
        Assert.Equal(dstBlack.Z, Z, 12);
    }

    [Fact]
    public void Source_white_maps_to_dest_white_exactly()
    {
        XyzNumber srcBlack = new(0.005, 0.005, 0.005);
        XyzNumber dstBlack = new(0.001, 0.001, 0.001);
        MatrixTransform bpc = BlackPointCompensation.Build(srcBlack, dstBlack);

        (double X, double Y, double Z) = bpc.Transform(
            StandardIlluminants.D50.X, StandardIlluminants.D50.Y, StandardIlluminants.D50.Z);
        Assert.Equal(StandardIlluminants.D50.X, X, 9);
        Assert.Equal(StandardIlluminants.D50.Y, Y, 9);
        Assert.Equal(StandardIlluminants.D50.Z, Z, 9);
    }

    [Fact]
    public void Identical_endpoints_yield_identity_transform()
    {
        XyzNumber black = new(0.005, 0.005, 0.005);
        MatrixTransform bpc = BlackPointCompensation.Build(black, black);

        (double X, double Y, double Z) = bpc.Transform(0.42, 0.5, 0.78);
        Assert.Equal(0.42, X, 12);
        Assert.Equal(0.5, Y, 12);
        Assert.Equal(0.78, Z, 12);
    }

    [Fact]
    public void Source_black_lighter_than_dest_compresses_shadows()
    {
        // If source black is at Y=0.05 and dest black is at Y=0, then shadows below Y=0.05 in the
        // source domain expand to fill dest range, while source values above 0.05 get scaled toward
        // dest black less aggressively.
        XyzNumber srcBlack = new(0.05, 0.05, 0.05);
        XyzNumber dstBlack = new(0.0, 0.0, 0.0);
        MatrixTransform bpc = BlackPointCompensation.Build(srcBlack, dstBlack);

        // Source midpoint (Y = 0.525, exactly halfway between src_black and white): expect dest
        // Y to also be the halfway point between 0 and 1, which is 0.5.
        double srcMidY = 0.5 * (srcBlack.Y + StandardIlluminants.D50.Y);
        (_, double Y, _) = bpc.Transform(srcMidY, srcMidY, srcMidY);
        Assert.Equal(0.5, Y, 9);
    }

    [Fact]
    public void Inverse_round_trips()
    {
        XyzNumber srcBlack = new(0.008, 0.010, 0.012);
        XyzNumber dstBlack = new(0.003, 0.004, 0.005);
        MatrixTransform bpc = BlackPointCompensation.Build(srcBlack, dstBlack);
        MatrixTransform inv = bpc.Inverse();

        (double tX, double tY, double tZ) = bpc.Transform(0.3, 0.4, 0.5);
        (double bX, double bY, double bZ) = inv.Transform(tX, tY, tZ);
        Assert.Equal(0.3, bX, 9);
        Assert.Equal(0.4, bY, 9);
        Assert.Equal(0.5, bZ, 9);
    }

    [Fact]
    public void Full_overload_uses_custom_white_points()
    {
        // Use D65 as source white instead of D50.
        XyzNumber srcBlack = new(0.01, 0.01, 0.01);
        XyzNumber dstBlack = new(0.005, 0.005, 0.005);
        MatrixTransform bpc = BlackPointCompensation.Build(
            srcBlack, StandardIlluminants.D65,
            dstBlack, StandardIlluminants.D50);

        // Source white (D65) should land on dest white (D50).
        (double X, double Y, double Z) = bpc.Transform(
            StandardIlluminants.D65.X, StandardIlluminants.D65.Y, StandardIlluminants.D65.Z);
        Assert.Equal(StandardIlluminants.D50.X, X, 9);
        Assert.Equal(StandardIlluminants.D50.Y, Y, 9);
        Assert.Equal(StandardIlluminants.D50.Z, Z, 9);
    }
}
