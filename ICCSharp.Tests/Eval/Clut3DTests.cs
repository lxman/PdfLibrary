using ICCSharp.Eval;
using ICCSharp.Tags;

namespace ICCSharp.Tests.Eval;

public class Clut3DTests
{
    /// <summary>
    /// Builds a g×g×g CLUT with <paramref name="outputChannels"/> per node, populated by
    /// <paramref name="f"/>(r, g, b, channel) where each input is the grid-normalized coord.
    /// </summary>
    private static Clut3D BuildClut(int g, int outputChannels, Func<double, double, double, int, double> f)
    {
        var values = new double[g * g * g * outputChannels];
        var idx = 0;
        for (var i = 0; i < g; i++)
        for (var j = 0; j < g; j++)
        for (var k = 0; k < g; k++)
        {
            double r = i / (double)(g - 1);
            double gv = j / (double)(g - 1);
            double b = k / (double)(g - 1);
            for (var c = 0; c < outputChannels; c++)
                values[idx++] = f(r, gv, b, c);
        }
        byte[] grid = { (byte)g, (byte)g, (byte)g };
        LutClutData data = new(grid, 2, values, outputChannels);
        return new Clut3D(data);
    }

    // --- Identity --------------------------------------------------------

    [Fact]
    public void Identity_clut_returns_input_at_grid_corners()
    {
        Clut3D clut = BuildClut(g: 2, outputChannels: 3, (r, g, b, c) => c switch { 0 => r, 1 => g, _ => b });
        double[] r1 = clut.Apply(0, 0, 0);
        Assert.Equal(0.0, r1[0]); Assert.Equal(0.0, r1[1]); Assert.Equal(0.0, r1[2]);

        double[] r2 = clut.Apply(1, 1, 1);
        Assert.Equal(1.0, r2[0]); Assert.Equal(1.0, r2[1]); Assert.Equal(1.0, r2[2]);

        double[] r3 = clut.Apply(1, 0, 0);
        Assert.Equal(1.0, r3[0]); Assert.Equal(0.0, r3[1]); Assert.Equal(0.0, r3[2]);
    }

    [Fact]
    public void Identity_clut_preserves_interior_points()
    {
        Clut3D clut = BuildClut(g: 17, outputChannels: 3, (r, g, b, c) => c switch { 0 => r, 1 => g, _ => b });
        // Tetrahedral on an identity CLUT must be exact for every input.
        foreach ((double r, double g, double b) in new[]
        {
            (0.25, 0.5, 0.75),
            (0.1, 0.9, 0.5),
            (0.333, 0.666, 0.5),
            (0.0, 0.5, 1.0),
        })
        {
            double[] result = clut.Apply(r, g, b);
            Assert.Equal(r, result[0], 9);
            Assert.Equal(g, result[1], 9);
            Assert.Equal(b, result[2], 9);
        }
    }

    [Fact]
    public void Tetrahedral_preserves_neutral_diagonal_exactly()
    {
        // Identity CLUT on g=3: tetrahedral should reproduce neutrals (r==g==b) exactly,
        // which is the property that motivates tetrahedral over trilinear in the first place.
        Clut3D clut = BuildClut(g: 3, outputChannels: 3, (r, g, b, c) => c switch { 0 => r, 1 => g, _ => b });
        for (var n = 0; n <= 20; n++)
        {
            double t = n / 20.0;
            double[] res = clut.Apply(t, t, t);
            Assert.Equal(t, res[0], 9);
            Assert.Equal(t, res[1], 9);
            Assert.Equal(t, res[2], 9);
        }
    }

    // --- Boundary clamping ----------------------------------------------

    [Fact]
    public void Inputs_outside_zero_one_are_clamped()
    {
        Clut3D clut = BuildClut(g: 2, outputChannels: 3, (r, g, b, c) => c switch { 0 => r, 1 => g, _ => b });
        double[] over = clut.Apply(2.0, 2.0, 2.0);
        Assert.Equal(1.0, over[0]); Assert.Equal(1.0, over[1]); Assert.Equal(1.0, over[2]);

        double[] under = clut.Apply(-1.0, -1.0, -1.0);
        Assert.Equal(0.0, under[0]); Assert.Equal(0.0, under[1]); Assert.Equal(0.0, under[2]);
    }

    // --- Known-value check ----------------------------------------------

    [Fact]
    public void Centre_of_constant_value_clut_returns_that_value()
    {
        // All grid points set to (0.42, 0.42, 0.42). Tetrahedral of constant cube = constant.
        Clut3D clut = BuildClut(g: 5, outputChannels: 3, (_, _, _, _) => 0.42);
        double[] mid = clut.Apply(0.37, 0.62, 0.18);
        Assert.Equal(0.42, mid[0], 12);
        Assert.Equal(0.42, mid[1], 12);
        Assert.Equal(0.42, mid[2], 12);
    }

    [Fact]
    public void Linear_function_is_reproduced_exactly()
    {
        // Output value = ar + bg + cb + d (affine). Tetrahedral matches it exactly because
        // each tetrahedron is a linear blend and the function is itself linear.
        Clut3D clut = BuildClut(g: 4, outputChannels: 1,
            (r, g, b, _) => 0.1 + 0.3 * r + 0.4 * g + 0.2 * b);
        foreach ((double r, double g, double b) in new[]
        {
            (0.1, 0.2, 0.3),
            (0.5, 0.5, 0.5),
            (0.9, 0.1, 0.7),
        })
        {
            double expected = 0.1 + 0.3 * r + 0.4 * g + 0.2 * b;
            double actual = clut.Apply(r, g, b)[0];
            Assert.Equal(expected, actual, 12);
        }
    }

    // --- Asymmetric grid ------------------------------------------------

    [Fact]
    public void Different_grid_sizes_per_axis_are_supported()
    {
        // 2×3×4 grid, single output channel = r-coordinate.
        int gR = 2, gG = 3, gB = 4;
        var o = 1;
        var values = new double[gR * gG * gB * o];
        var idx = 0;
        for (var i = 0; i < gR; i++)
        for (var j = 0; j < gG; j++)
        for (var k = 0; k < gB; k++)
            values[idx++] = i / (double)(gR - 1);

        Clut3D clut = new(new LutClutData(new byte[] { (byte)gR, (byte)gG, (byte)gB }, 2, values, o));
        Assert.Equal(0.0, clut.Apply(0.0, 0.5, 0.5)[0], 9);
        Assert.Equal(1.0, clut.Apply(1.0, 0.5, 0.5)[0], 9);
        Assert.Equal(0.5, clut.Apply(0.5, 0.5, 0.5)[0], 9);
    }

    // --- Zero-alloc API --------------------------------------------------

    [Fact]
    public void Span_overload_writes_into_caller_buffer()
    {
        Clut3D clut = BuildClut(g: 3, outputChannels: 3, (r, g, b, c) => c switch { 0 => r, 1 => g, _ => b });
        Span<double> output = stackalloc double[3];
        clut.Apply(0.25, 0.5, 0.75, output);
        Assert.Equal(0.25, output[0], 9);
        Assert.Equal(0.5,  output[1], 9);
        Assert.Equal(0.75, output[2], 9);
    }

    [Fact]
    public void Span_overload_throws_when_buffer_too_small()
    {
        Clut3D clut = BuildClut(g: 2, outputChannels: 3, (_, _, _, _) => 0.0);
        Assert.Throws<ArgumentException>(() =>
        {
            var tooSmall = new double[2];
            clut.Apply(0.5, 0.5, 0.5, tooSmall);
        });
    }

    // --- Construction validation ----------------------------------------

    [Fact]
    public void Non_three_dim_clut_rejected()
    {
        byte[] grid2 = { 2, 2 };
        Assert.Throws<ArgumentException>(() => new Clut3D(new LutClutData(grid2, 2, new double[2 * 2 * 1], 1)));
    }

    [Fact]
    public void Wrong_value_count_rejected()
    {
        byte[] grid = { 2, 2, 2 };
        Assert.Throws<ArgumentException>(() => new Clut3D(new LutClutData(grid, 2, new double[7], 1)));
    }

    [Fact]
    public void Grid_dimension_below_two_rejected()
    {
        // A 3-D grid with a single-sample axis can't be interpolated (floor index + 1 falls off the
        // axis). The value count still matches (1×2×2×1 = 4), so only an explicit guard rejects it.
        byte[] grid = { 1, 2, 2 };
        Assert.Throws<ArgumentException>(() => new Clut3D(new LutClutData(grid, 2, new double[1 * 2 * 2 * 1], 1)));
    }
}
