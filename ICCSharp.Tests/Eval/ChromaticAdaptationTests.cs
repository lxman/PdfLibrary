using ICCSharp.Eval;
using ICCSharp.IO;

namespace ICCSharp.Tests.Eval;

public class ChromaticAdaptationTests
{
    [Fact]
    public void Same_white_returns_identity_within_rounding()
    {
        Matrix3x3 m = ChromaticAdaptation.ComputeMatrix(StandardIlluminants.D50, StandardIlluminants.D50);
        Assert.Equal(1.0, m.M00, 12);
        Assert.Equal(1.0, m.M11, 12);
        Assert.Equal(1.0, m.M22, 12);
        Assert.Equal(0.0, m.M01, 12);
        Assert.Equal(0.0, m.M02, 12);
        Assert.Equal(0.0, m.M12, 12);
    }

    [Fact]
    public void Bradford_maps_source_white_to_dest_white_exactly()
    {
        Matrix3x3 m = ChromaticAdaptation.ComputeMatrix(StandardIlluminants.D65, StandardIlluminants.D50);
        (double X, double Y, double Z) = m.Transform(
            StandardIlluminants.D65.X, StandardIlluminants.D65.Y, StandardIlluminants.D65.Z);
        Assert.Equal(StandardIlluminants.D50.X, X, 9);
        Assert.Equal(StandardIlluminants.D50.Y, Y, 9);
        Assert.Equal(StandardIlluminants.D50.Z, Z, 9);
    }

    [Fact]
    public void Bradford_D65_to_D50_matches_published_reference()
    {
        // Reference matrix from the ICC chromatic adaptation TN ("Chromatic Adaptation in Color
        // Image Reproduction"). Values to 4 decimals are stable across most published sources.
        Matrix3x3 m = ChromaticAdaptation.ComputeMatrix(StandardIlluminants.D65, StandardIlluminants.D50);
        Assert.Equal( 1.0479, m.M00, 3);
        Assert.Equal( 0.0229, m.M01, 3);
        Assert.Equal(-0.0501, m.M02, 3);
        Assert.Equal( 0.0296, m.M10, 3);
        Assert.Equal( 0.9904, m.M11, 3);
        Assert.Equal(-0.0170, m.M12, 3);
        Assert.Equal(-0.0092, m.M20, 3);
        Assert.Equal( 0.0150, m.M21, 3);
        Assert.Equal( 0.7518, m.M22, 3);
    }

    [Fact]
    public void Bradford_round_trip_D65_to_D50_to_D65_recovers_input()
    {
        Matrix3x3 forward = ChromaticAdaptation.ComputeMatrix(StandardIlluminants.D65, StandardIlluminants.D50);
        Matrix3x3 reverse = ChromaticAdaptation.ComputeMatrix(StandardIlluminants.D50, StandardIlluminants.D65);
        Matrix3x3 product = forward.Multiply(reverse); // round trip via D50

        (double X, double Y, double Z) = product.Transform(0.3, 0.4, 0.5);
        Assert.Equal(0.3, X, 9);
        Assert.Equal(0.4, Y, 9);
        Assert.Equal(0.5, Z, 9);
    }

    [Fact]
    public void Cat02_also_maps_source_white_to_dest_white()
    {
        Matrix3x3 m = ChromaticAdaptation.ComputeMatrix(StandardIlluminants.D65, StandardIlluminants.D50, CatMethod.Cat02);
        (double X, double Y, double Z) = m.Transform(
            StandardIlluminants.D65.X, StandardIlluminants.D65.Y, StandardIlluminants.D65.Z);
        Assert.Equal(StandardIlluminants.D50.X, X, 9);
        Assert.Equal(StandardIlluminants.D50.Y, Y, 9);
        Assert.Equal(StandardIlluminants.D50.Z, Z, 9);
    }

    [Fact]
    public void Bradford_and_cat02_diverge_for_non_neutral_colors()
    {
        // The methods agree on neutrals (white→white by construction) but should differ on
        // saturated colors — this is what makes one method "better" than another for a given
        // viewing condition.
        XyzNumber saturated = new(0.4, 0.2, 0.1);
        Matrix3x3 bfd = ChromaticAdaptation.ComputeMatrix(StandardIlluminants.D65, StandardIlluminants.D50);
        Matrix3x3 c02 = ChromaticAdaptation.ComputeMatrix(StandardIlluminants.D65, StandardIlluminants.D50, CatMethod.Cat02);

        (double bX, _, _) = bfd.Transform(saturated.X, saturated.Y, saturated.Z);
        (double cX, _, _) = c02.Transform(saturated.X, saturated.Y, saturated.Z);
        Assert.NotEqual(bX, cX);
    }

    [Fact]
    public void XyzScaling_is_just_diagonal_white_ratios()
    {
        Matrix3x3 m = ChromaticAdaptation.ComputeMatrix(
            StandardIlluminants.D65, StandardIlluminants.D50, CatMethod.XyzScaling);
        Assert.Equal(StandardIlluminants.D50.X / StandardIlluminants.D65.X, m.M00, 9);
        Assert.Equal(StandardIlluminants.D50.Y / StandardIlluminants.D65.Y, m.M11, 9);
        Assert.Equal(StandardIlluminants.D50.Z / StandardIlluminants.D65.Z, m.M22, 9);
        Assert.Equal(0.0, m.M01, 12);
        Assert.Equal(0.0, m.M12, 12);
    }
}
