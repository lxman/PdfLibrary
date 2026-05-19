using ICCSharp.Eval;
using ICCSharp.IO;

namespace ICCSharp.Tests.Eval;

public class Matrix3x3Tests
{
    [Fact]
    public void Identity_transforms_vector_unchanged()
    {
        (double x, double y, double z) = Matrix3x3.Identity.Transform(0.3, 0.5, 0.7);
        Assert.Equal(0.3, x); Assert.Equal(0.5, y); Assert.Equal(0.7, z);
    }

    [Fact]
    public void Determinant_of_identity_is_one()
    {
        Assert.Equal(1.0, Matrix3x3.Identity.Determinant, 12);
    }

    [Fact]
    public void Determinant_known_matrix()
    {
        // |1 2 3; 0 1 4; 5 6 0| = 1
        Matrix3x3 m = new(1, 2, 3, 0, 1, 4, 5, 6, 0);
        Assert.Equal(1.0, m.Determinant, 9);
    }

    [Fact]
    public void Inverse_round_trip_recovers_input()
    {
        Matrix3x3 m = new(1, 2, 3, 0, 1, 4, 5, 6, 0);
        Matrix3x3 inv = m.Inverse();

        (double x, double y, double z) = (0.4, 0.6, 0.8);
        (double tx, double ty, double tz) = m.Transform(x, y, z);
        (double bx, double by, double bz) = inv.Transform(tx, ty, tz);
        Assert.Equal(x, bx, 10);
        Assert.Equal(y, by, 10);
        Assert.Equal(z, bz, 10);
    }

    [Fact]
    public void Inverse_of_identity_is_identity()
    {
        Matrix3x3 inv = Matrix3x3.Identity.Inverse();
        Assert.Equal(Matrix3x3.Identity, inv);
    }

    [Fact]
    public void Inverse_of_singular_matrix_throws()
    {
        // All rows equal — rank 1.
        Matrix3x3 m = new(1, 2, 3, 1, 2, 3, 1, 2, 3);
        Assert.Throws<InvalidOperationException>(() => m.Inverse());
    }

    [Fact]
    public void Multiply_associativity_holds()
    {
        Matrix3x3 a = new(1, 2, 3, 4, 5, 6, 7, 8, 10);
        Matrix3x3 b = new(2, 0, 1, 0, 3, 0, 1, 0, 2);
        Matrix3x3 c = a.Multiply(b);

        (double tx, double ty, double tz) = b.Transform(1.0, 0.0, 0.0);
        (double rx, double ry, double rz) = a.Transform(tx, ty, tz);
        (double cx, double cy, double cz) = c.Transform(1.0, 0.0, 0.0);
        Assert.Equal(rx, cx, 12);
        Assert.Equal(ry, cy, 12);
        Assert.Equal(rz, cz, 12);
    }

    [Fact]
    public void FromColorants_builds_RGB_to_XYZ_matrix()
    {
        // sRGB D65 reference primaries (XYZ values for unit RGB).
        XyzNumber r = new(0.4124564, 0.2126729, 0.0193339);
        XyzNumber g = new(0.3575761, 0.7151522, 0.1191920);
        XyzNumber b = new(0.1804375, 0.0721750, 0.9503041);

        Matrix3x3 m = Matrix3x3.FromColorants(r, g, b);

        // White RGB (1,1,1) should equal the sum of the three colorants ≈ D65 reference white.
        (double X, double Y, double Z) = m.Transform(1.0, 1.0, 1.0);
        Assert.Equal(r.X + g.X + b.X, X, 9);
        Assert.Equal(r.Y + g.Y + b.Y, Y, 9);
        Assert.Equal(r.Z + g.Z + b.Z, Z, 9);

        // Unit red column = red colorant.
        (double rX, double rY, double rZ) = m.Transform(1, 0, 0);
        Assert.Equal(r.X, rX, 9);
        Assert.Equal(r.Y, rY, 9);
        Assert.Equal(r.Z, rZ, 9);
    }

    [Fact]
    public void FromRowMajor_round_trips_through_indices()
    {
        double[] a = { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        Matrix3x3 m = Matrix3x3.FromRowMajor(a);
        Assert.Equal(1.0, m.M00);
        Assert.Equal(5.0, m.M11);
        Assert.Equal(9.0, m.M22);
        Assert.Equal(2.0, m.M01);
        Assert.Equal(4.0, m.M10);
    }
}
