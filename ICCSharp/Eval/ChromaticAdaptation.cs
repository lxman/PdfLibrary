using System;
using ICCSharp.IO;

namespace ICCSharp.Eval;

/// <summary>
/// Builds the 3×3 chromatic adaptation transform that maps colors viewed under one reference
/// white to their appearance equivalents under another. The transform M_CAT is constructed as:
///   M_CAT = M_cone^-1 · diag(dst_cone / src_cone) · M_cone
/// where M_cone is the cone-response basis matrix (Bradford, CAT02, or identity for XYZ scaling).
///
/// By construction <c>M_CAT · sourceWhite = destWhite</c>.
/// </summary>
public static class ChromaticAdaptation
{
    private static readonly Matrix3x3 BradfordM = new(
         0.8951,  0.2664, -0.1614,
        -0.7502,  1.7135,  0.0367,
         0.0389, -0.0685,  1.0296);

    private static readonly Matrix3x3 Cat02M = new(
         0.7328,  0.4296, -0.1624,
        -0.7036,  1.6975,  0.0061,
         0.0030,  0.0136,  0.9834);

    /// <summary>Builds the adaptation matrix from <paramref name="sourceWhite"/> to <paramref name="destWhite"/>.</summary>
    public static Matrix3x3 ComputeMatrix(
        XyzNumber sourceWhite, XyzNumber destWhite, CatMethod method = CatMethod.Bradford)
    {
        Matrix3x3 m = method switch
        {
            CatMethod.Bradford => BradfordM,
            CatMethod.Cat02 => Cat02M,
            CatMethod.XyzScaling => Matrix3x3.Identity,
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };

        (double sx, double sy, double sz) = m.Transform(sourceWhite.X, sourceWhite.Y, sourceWhite.Z);
        (double dx, double dy, double dz) = m.Transform(destWhite.X, destWhite.Y, destWhite.Z);

        if (sx == 0 || sy == 0 || sz == 0)
            throw new InvalidOperationException("Source white maps to a zero cone response.");

        Matrix3x3 scale = new(
            dx / sx, 0, 0,
            0, dy / sy, 0,
            0, 0, dz / sz);

        Matrix3x3 mInv = m.Inverse();
        return mInv.Multiply(scale).Multiply(m);
    }

    /// <summary>Convenience overload returning a <see cref="MatrixTransform"/> with zero offset.</summary>
    public static MatrixTransform Compute(
        XyzNumber sourceWhite, XyzNumber destWhite, CatMethod method = CatMethod.Bradford)
        => new(ComputeMatrix(sourceWhite, destWhite, method));
}
