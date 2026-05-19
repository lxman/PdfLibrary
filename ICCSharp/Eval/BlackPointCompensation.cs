using ICCSharp.IO;

namespace ICCSharp.Eval;

/// <summary>
/// Black-Point Compensation per Adobe TN (2006). Constructs a per-axis affine transform in XYZ
/// PCS such that:
///   <c>source_black → dest_black</c>
///   <c>source_white → dest_white</c>
/// All intermediate points scale linearly along each axis. Returned as a <see cref="MatrixTransform"/>
/// (diagonal linear part plus offset) so it composes with the other pipeline primitives.
///
/// Use case: when the source profile's device black maps to a PCS Y above zero and the destination
/// device's black is darker (or vice versa), naive end-to-end concatenation either crushes shadows
/// or wastes dynamic range. BPC rescales to use the destination's full range.
/// </summary>
public static class BlackPointCompensation
{
    public static MatrixTransform Build(
        XyzNumber sourceBlack, XyzNumber sourceWhite,
        XyzNumber destBlack,   XyzNumber destWhite)
    {
        double sx = (destWhite.X - destBlack.X) / (sourceWhite.X - sourceBlack.X);
        double sy = (destWhite.Y - destBlack.Y) / (sourceWhite.Y - sourceBlack.Y);
        double sz = (destWhite.Z - destBlack.Z) / (sourceWhite.Z - sourceBlack.Z);

        Matrix3x3 linear = new(
            sx, 0, 0,
            0, sy, 0,
            0, 0, sz);

        (double ox, double oy, double oz) = (
            destBlack.X - sx * sourceBlack.X,
            destBlack.Y - sy * sourceBlack.Y,
            destBlack.Z - sz * sourceBlack.Z);

        return new MatrixTransform(linear, (ox, oy, oz));
    }

    /// <summary>
    /// Convenience overload for the common case where both whites are D50 (ICC PCS). Pass only
    /// the two black points.
    /// </summary>
    public static MatrixTransform Build(XyzNumber sourceBlack, XyzNumber destBlack)
        => Build(sourceBlack, StandardIlluminants.D50, destBlack, StandardIlluminants.D50);
}
