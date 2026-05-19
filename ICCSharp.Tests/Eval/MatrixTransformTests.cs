using ICCSharp.Eval;

namespace ICCSharp.Tests.Eval;

public class MatrixTransformTests
{
    [Fact]
    public void FromMabArray_assigns_first_nine_to_linear_and_last_three_to_offset()
    {
        double[] a = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 0.1, 0.2, 0.3 };
        MatrixTransform t = MatrixTransform.FromMabArray(a);
        Assert.Equal(1.0, t.Linear.M00);
        Assert.Equal(9.0, t.Linear.M22);
        Assert.Equal(0.1, t.Offset.X);
        Assert.Equal(0.2, t.Offset.Y);
        Assert.Equal(0.3, t.Offset.Z);
    }

    [Fact]
    public void Transform_applies_linear_then_offset()
    {
        double[] a = { 2, 0, 0, 0, 3, 0, 0, 0, 5, 1, 2, 3 };
        MatrixTransform t = MatrixTransform.FromMabArray(a);
        (double x, double y, double z) = t.Transform(0.5, 0.5, 0.5);
        Assert.Equal(2.0, x, 12); // 2*0.5 + 1
        Assert.Equal(3.5, y, 12); // 3*0.5 + 2
        Assert.Equal(5.5, z, 12); // 5*0.5 + 3
    }

    [Fact]
    public void Inverse_round_trip_recovers_input()
    {
        double[] a = { 1, 2, 3, 0, 1, 4, 5, 6, 0, 0.1, 0.2, 0.3 };
        MatrixTransform t = MatrixTransform.FromMabArray(a);
        MatrixTransform inv = t.Inverse();

        (double x, double y, double z) = (0.4, 0.6, 0.8);
        (double tx, double ty, double tz) = t.Transform(x, y, z);
        (double bx, double by, double bz) = inv.Transform(tx, ty, tz);
        Assert.Equal(x, bx, 10);
        Assert.Equal(y, by, 10);
        Assert.Equal(z, bz, 10);
    }

    [Fact]
    public void Then_composes_in_correct_order()
    {
        // t1: scale by 2; t2: translate by (10, 20, 30).
        MatrixTransform t1 = new(new Matrix3x3(2, 0, 0, 0, 2, 0, 0, 0, 2));
        MatrixTransform t2 = new(Matrix3x3.Identity, (10, 20, 30));
        MatrixTransform composed = t1.Then(t2);

        // composed(v) = t2(t1(v)). For v=(1,1,1): t1 → (2,2,2); t2 → (12, 22, 32).
        (double x, double y, double z) = composed.Transform(1, 1, 1);
        Assert.Equal(12, x); Assert.Equal(22, y); Assert.Equal(32, z);
    }

    [Fact]
    public void Identity_is_no_op()
    {
        (double x, double y, double z) = MatrixTransform.Identity.Transform(0.123, 0.456, 0.789);
        Assert.Equal(0.123, x);
        Assert.Equal(0.456, y);
        Assert.Equal(0.789, z);
    }

    [Fact]
    public void Inverse_of_singular_linear_throws()
    {
        double[] a = { 1, 2, 3, 1, 2, 3, 1, 2, 3, 0, 0, 0 };
        MatrixTransform t = MatrixTransform.FromMabArray(a);
        Assert.Throws<InvalidOperationException>(() => t.Inverse());
    }
}
