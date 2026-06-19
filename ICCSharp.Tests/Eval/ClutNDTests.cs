using ICCSharp.Eval;
using ICCSharp.Tags;

namespace ICCSharp.Tests.Eval;

public class ClutNDTests
{
    private static ClutND BuildClut(int[] gridPoints, int outputChannels, Func<double[], int, double> f)
    {
        long total = 1; foreach (int g in gridPoints) total *= g;
        var values = new double[total * outputChannels];

        var idx = new int[gridPoints.Length];
        var flat = 0;
        while (true)
        {
            var coords = new double[gridPoints.Length];
            for (var i = 0; i < gridPoints.Length; i++)
                coords[i] = idx[i] / (double)(gridPoints[i] - 1);
            for (var c = 0; c < outputChannels; c++)
                values[flat++] = f(coords, c);

            int dim = gridPoints.Length - 1;
            while (dim >= 0)
            {
                idx[dim]++;
                if (idx[dim] < gridPoints[dim]) break;
                idx[dim] = 0;
                dim--;
            }
            if (dim < 0) break;
        }

        var gb = new byte[gridPoints.Length];
        for (var i = 0; i < gridPoints.Length; i++) gb[i] = (byte)gridPoints[i];
        return new ClutND(new LutClutData(gb, 2, values, outputChannels));
    }

    // --- 1D ---------------------------------------------------------------

    [Fact]
    public void OneD_linear_function_is_exact()
    {
        ClutND clut = BuildClut(new[] { 17 }, 1, (c, _) => 3 * c[0] + 1);
        Assert.Equal(1.0, clut.Apply(new[] { 0.0 })[0], 9);
        Assert.Equal(4.0, clut.Apply(new[] { 1.0 })[0], 9);
        Assert.Equal(2.5, clut.Apply(new[] { 0.5 })[0], 9);
    }

    // --- 2D ---------------------------------------------------------------

    [Fact]
    public void TwoD_bilinear_known_values()
    {
        // 2×2 with row-major layout: values[(i0 * 2 + i1)] so first dim varies slowest.
        // values = {0, 1, 2, 4}:
        //   V(0,0)=0  V(0,1)=1
        //   V(1,0)=2  V(1,1)=4
        // At (0.5, 0.5): 0.25 * (0+1+2+4) = 1.75.
        double[] values = { 0, 1, 2, 4 };
        ClutND clut = new(new LutClutData(new byte[] { 2, 2 }, 2, values, 1));

        Assert.Equal(0.0,  clut.Apply(new[] { 0.0, 0.0 })[0], 9);
        Assert.Equal(2.0,  clut.Apply(new[] { 1.0, 0.0 })[0], 9);
        Assert.Equal(1.0,  clut.Apply(new[] { 0.0, 1.0 })[0], 9);
        Assert.Equal(4.0,  clut.Apply(new[] { 1.0, 1.0 })[0], 9);
        Assert.Equal(1.75, clut.Apply(new[] { 0.5, 0.5 })[0], 9);
    }

    // --- 3D multilinear vs Clut3D tetrahedral ----------------------------

    [Fact]
    public void ThreeD_multilinear_agrees_with_clut3d_on_linear_function()
    {
        // For purely linear (affine) data both algorithms produce identical results
        // (it's the non-linear behaviour around the cube interior that differs).
        Clut3D tet = BuildLinear3D();
        ClutND multi = BuildClut(new[] { 4, 4, 4 }, 1,
            (c, _) => 0.1 + 0.3 * c[0] + 0.4 * c[1] + 0.2 * c[2]);
        foreach ((double r, double g, double b) in new[]
        {
            (0.25, 0.5, 0.75), (0.1, 0.9, 0.3), (0.5, 0.5, 0.5),
        })
        {
            double[] tetOut = tet.Apply(r, g, b);
            double[] multiOut = multi.Apply(new[] { r, g, b });
            Assert.Equal(tetOut[0], multiOut[0], 12);
        }
    }

    private static Clut3D BuildLinear3D()
    {
        var g = 4;
        var values = new double[g * g * g];
        var idx = 0;
        for (var i = 0; i < g; i++)
        for (var j = 0; j < g; j++)
        for (var k = 0; k < g; k++)
        {
            double r = i / 3.0, gv = j / 3.0, b = k / 3.0;
            values[idx++] = 0.1 + 0.3 * r + 0.4 * gv + 0.2 * b;
        }
        return new Clut3D(new LutClutData(new byte[] { 4, 4, 4 }, 2, values, 1));
    }

    [Fact]
    public void ThreeD_multilinear_off_diagonal_differs_from_tetrahedral()
    {
        // Sharp non-linear case: V111 = 1, all other corners = 0. Multilinear at (0.5, 0.5, 0.5)
        // gives 1/8; tetrahedral gives ½·db = 0.5 (since dr=dg=db=0.5 lands on a degenerate edge,
        // case dr>=dg>=db → V000*(1-0.5) + V100*0 + V110*0 + V111*0.5 = 0.5). They MUST differ.
        // This codifies the documented design choice between the two interpolators.
        double[] values = { 0, 0, 0, 0, 0, 0, 0, 1 };
        ClutND multi = new(new LutClutData(new byte[] { 2, 2, 2 }, 2, values, 1));
        Clut3D tet = new(new LutClutData(new byte[] { 2, 2, 2 }, 2, values, 1));

        double m = multi.Apply(new[] { 0.5, 0.5, 0.5 })[0];
        double t = tet.Apply(0.5, 0.5, 0.5)[0];
        Assert.Equal(0.125, m, 9);
        Assert.Equal(0.5, t, 9);
        Assert.NotEqual(m, t);
    }

    // --- 4D (CMYK-style) -------------------------------------------------

    [Fact]
    public void FourD_identity_returns_first_three_inputs_when_output_is_first_three()
    {
        // 4-input × 3-output, output channels = (c0, c1, c2) (i.e. ignore the 4th input — like
        // CMYK→XYZ where K just darkens). Linear function in 4 dims.
        ClutND clut = BuildClut(new[] { 3, 3, 3, 3 }, 3, (coords, c) => coords[c]);
        double[] result = clut.Apply(new[] { 0.2, 0.5, 0.8, 0.4 });
        Assert.Equal(0.2, result[0], 9);
        Assert.Equal(0.5, result[1], 9);
        Assert.Equal(0.8, result[2], 9);
    }

    [Fact]
    public void FourD_affine_function_is_exact()
    {
        // f(c, m, y, k) = 1 - k * (1 - 0.3c - 0.5m - 0.1y) (roughly approximates the CMYK→K relationship)
        // Multilinear on affine data is exact for any input.
        ClutND clut = BuildClut(new[] { 5, 5, 5, 5 }, 1,
            (c, _) => 0.1 + 0.2 * c[0] - 0.15 * c[1] + 0.3 * c[2] + 0.05 * c[3]);
        double[] input = { 0.4, 0.3, 0.7, 0.2 };
        double expected = 0.1 + 0.2 * input[0] - 0.15 * input[1] + 0.3 * input[2] + 0.05 * input[3];
        Assert.Equal(expected, clut.Apply(input)[0], 9);
    }

    // --- Boundary clamping -----------------------------------------------

    [Fact]
    public void Inputs_outside_zero_one_are_clamped_per_axis()
    {
        ClutND clut = BuildClut(new[] { 2, 2 }, 1, (c, _) => c[0] + c[1]);
        Assert.Equal(2.0, clut.Apply(new[] {  5.0,  5.0 })[0], 9);
        Assert.Equal(0.0, clut.Apply(new[] { -1.0, -1.0 })[0], 9);
        Assert.Equal(1.0, clut.Apply(new[] {  5.0, -1.0 })[0], 9);
    }

    // --- Construction validation ----------------------------------------

    [Fact]
    public void Empty_grid_dim_rejected()
    {
        Assert.Throws<ArgumentException>(() => new ClutND(new LutClutData(new byte[] { 1, 2 }, 2, new double[2], 1)));
    }

    [Fact]
    public void Too_many_dims_rejected()
    {
        var tooMany = new byte[ClutND.MaxInputDims + 1];
        for (var i = 0; i < tooMany.Length; i++) tooMany[i] = 2;
        long total = 1L << tooMany.Length;
        Assert.Throws<ArgumentException>(() => new ClutND(new LutClutData(tooMany, 2, new double[total], 1)));
    }

    [Fact]
    public void Wrong_value_count_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new ClutND(new LutClutData(new byte[] { 2, 2, 2 }, 2, new double[7], 1)));
    }

    [Fact]
    public void Apply_with_wrong_input_count_throws()
    {
        ClutND clut = BuildClut(new[] { 2, 2 }, 1, (_, _) => 0.0);
        Assert.Throws<ArgumentException>(() => clut.Apply(new[] { 0.5 }));
    }
}
