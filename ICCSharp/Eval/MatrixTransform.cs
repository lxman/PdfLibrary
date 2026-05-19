using System;

namespace ICCSharp.Eval;

/// <summary>
/// Affine 3×4 transform: a 3×3 linear part plus a 3-element translation offset.
///   out = Linear · in + Offset
/// This is the shape consumed by mAB/mBA tag matrices (length-12 row-major: 9 linear + 3 offset)
/// and by any PCS-side translation in the pipeline. With offset = (0,0,0) it degenerates to a
/// pure 3×3 transform.
/// </summary>
public sealed class MatrixTransform
{
    public Matrix3x3 Linear { get; }
    public (double X, double Y, double Z) Offset { get; }

    public MatrixTransform(Matrix3x3 linear) : this(linear, (0, 0, 0)) { }

    public MatrixTransform(Matrix3x3 linear, (double X, double Y, double Z) offset)
    {
        Linear = linear;
        Offset = offset;
    }

    /// <summary>
    /// Builds from a 12-element row-major array as stored in mAB/mBA matrix blocks:
    /// indices 0..8 = 3×3 linear; 9..11 = offset.
    /// </summary>
    public static MatrixTransform FromMabArray(double[] m)
    {
        if (m is null) throw new ArgumentNullException(nameof(m));
        if (m.Length != 12) throw new ArgumentException("mAB matrix array must have 12 elements.", nameof(m));
        Matrix3x3 lin = new(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);
        return new MatrixTransform(lin, (m[9], m[10], m[11]));
    }

    public static MatrixTransform Identity => new(Matrix3x3.Identity);

    /// <summary>Forward: out = Linear · in + Offset.</summary>
    public (double X, double Y, double Z) Transform(double x, double y, double z)
    {
        (double tx, double ty, double tz) = Linear.Transform(x, y, z);
        return (tx + Offset.X, ty + Offset.Y, tz + Offset.Z);
    }

    /// <summary>
    /// Inverse transform: in = Linear^-1 · (out - Offset). Throws if the linear part is singular.
    /// </summary>
    public MatrixTransform Inverse(double tolerance = 1e-12)
    {
        Matrix3x3 inv = Linear.Inverse(tolerance);
        (double ox, double oy, double oz) = inv.Transform(-Offset.X, -Offset.Y, -Offset.Z);
        return new MatrixTransform(inv, (ox, oy, oz));
    }

    /// <summary>Composes this transform after another: (this ∘ other)(v) = this(other(v)).</summary>
    public MatrixTransform Then(MatrixTransform other)
    {
        Matrix3x3 lin = other.Linear.Multiply(Linear);
        (double tx, double ty, double tz) = other.Linear.Transform(Offset.X, Offset.Y, Offset.Z);
        return new MatrixTransform(lin, (tx + other.Offset.X, ty + other.Offset.Y, tz + other.Offset.Z));
    }
}
