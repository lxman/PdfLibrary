using System;
using ICCSharp.IO;

namespace ICCSharp.Eval;

/// <summary>
/// Immutable 3×3 matrix stored in row-major order. Used pervasively by ICC color math: PCS-XYZ
/// matrices, chromatic adaptation transforms, mAB/mBA matrix blocks, colorant matrices built from
/// rXYZ/gXYZ/bXYZ tags. Operations needed at the CMM layer are determinant, inverse, multiply,
/// vector-transform.
/// </summary>
public readonly struct Matrix3x3 : IEquatable<Matrix3x3>
{
    public double M00 { get; } public double M01 { get; } public double M02 { get; }
    public double M10 { get; } public double M11 { get; } public double M12 { get; }
    public double M20 { get; } public double M21 { get; } public double M22 { get; }

    public Matrix3x3(
        double m00, double m01, double m02,
        double m10, double m11, double m12,
        double m20, double m21, double m22)
    {
        M00 = m00; M01 = m01; M02 = m02;
        M10 = m10; M11 = m11; M12 = m12;
        M20 = m20; M21 = m21; M22 = m22;
    }

    /// <summary>Builds from a 9-element row-major array.</summary>
    public static Matrix3x3 FromRowMajor(double[] m)
    {
        if (m is null) throw new ArgumentNullException(nameof(m));
        if (m.Length != 9) throw new ArgumentException("Matrix array must have 9 elements.", nameof(m));
        return new Matrix3x3(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);
    }

    /// <summary>
    /// Builds the device-RGB → PCS-XYZ matrix whose columns are the three colorant XYZ values.
    /// This is what the v2 matrix/TRC tag family (rXYZ/gXYZ/bXYZ) is laid out for.
    /// </summary>
    public static Matrix3x3 FromColorants(XyzNumber red, XyzNumber green, XyzNumber blue)
        => new(
            red.X, green.X, blue.X,
            red.Y, green.Y, blue.Y,
            red.Z, green.Z, blue.Z);

    public static Matrix3x3 Identity => new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    public double Determinant =>
        M00 * (M11 * M22 - M12 * M21)
      - M01 * (M10 * M22 - M12 * M20)
      + M02 * (M10 * M21 - M11 * M20);

    /// <summary>Cofactor-based inverse. Throws when the matrix is singular within <paramref name="tolerance"/>.</summary>
    public Matrix3x3 Inverse(double tolerance = 1e-12)
    {
        double det = Determinant;
        if (Math.Abs(det) < tolerance)
            throw new InvalidOperationException($"Matrix is singular (det = {det:G}).");
        double inv = 1.0 / det;
        return new Matrix3x3(
            (M11 * M22 - M12 * M21) * inv,
            (M02 * M21 - M01 * M22) * inv,
            (M01 * M12 - M02 * M11) * inv,
            (M12 * M20 - M10 * M22) * inv,
            (M00 * M22 - M02 * M20) * inv,
            (M02 * M10 - M00 * M12) * inv,
            (M10 * M21 - M11 * M20) * inv,
            (M01 * M20 - M00 * M21) * inv,
            (M00 * M11 - M01 * M10) * inv);
    }

    public Matrix3x3 Multiply(Matrix3x3 b)
        => new(
            M00 * b.M00 + M01 * b.M10 + M02 * b.M20,
            M00 * b.M01 + M01 * b.M11 + M02 * b.M21,
            M00 * b.M02 + M01 * b.M12 + M02 * b.M22,
            M10 * b.M00 + M11 * b.M10 + M12 * b.M20,
            M10 * b.M01 + M11 * b.M11 + M12 * b.M21,
            M10 * b.M02 + M11 * b.M12 + M12 * b.M22,
            M20 * b.M00 + M21 * b.M10 + M22 * b.M20,
            M20 * b.M01 + M21 * b.M11 + M22 * b.M21,
            M20 * b.M02 + M21 * b.M12 + M22 * b.M22);

    /// <summary>Applies the matrix to the 3-vector (x, y, z).</summary>
    public (double X, double Y, double Z) Transform(double x, double y, double z)
        => (M00 * x + M01 * y + M02 * z,
            M10 * x + M11 * y + M12 * z,
            M20 * x + M21 * y + M22 * z);

    public bool Equals(Matrix3x3 other) =>
        M00 == other.M00 && M01 == other.M01 && M02 == other.M02 &&
        M10 == other.M10 && M11 == other.M11 && M12 == other.M12 &&
        M20 == other.M20 && M21 == other.M21 && M22 == other.M22;

    public override bool Equals(object? obj) => obj is Matrix3x3 m && Equals(m);
    public override int GetHashCode() => HashCode.Combine(
        HashCode.Combine(M00, M01, M02), HashCode.Combine(M10, M11, M12), HashCode.Combine(M20, M21, M22));
}
