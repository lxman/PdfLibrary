using ICCSharp.Eval;
using ICCSharp.Tags;

namespace ICCSharp.Tests.Eval;

public class PipelineTests
{
    /// <summary>Builds an identity 'curv' element (count=0).</summary>
    private static CurveTagElement IdCurve() => new(Array.Empty<ushort>());

    /// <summary>Builds an mAB tag from already-parsed sections (skips the byte layout).</summary>
    private static LutAToBTagElement MakeMab(
        int i, int o,
        IReadOnlyList<TagElement>? aCurves = null,
        LutClutData? clut = null,
        IReadOnlyList<TagElement>? mCurves = null,
        double[]? matrix = null,
        IReadOnlyList<TagElement>? bCurves = null)
    {
        bCurves ??= Identity(o);
        return new LutAToBTagElement(i, o, aCurves, clut, mCurves, matrix, bCurves);
    }

    private static TagElement[] Identity(int n)
    {
        TagElement[] arr = new TagElement[n];
        for (int i = 0; i < n; i++) arr[i] = IdCurve();
        return arr;
    }

    // --- Minimal pipeline: B-curves only ---------------------------------

    [Fact]
    public void Mab_with_only_B_curves_is_identity()
    {
        LutAToBTagElement tag = MakeMab(3, 3);
        MabPipeline p = new(tag);
        double[] result = p.Apply(new[] { 0.2, 0.5, 0.8 });
        Assert.Equal(0.2, result[0], 12);
        Assert.Equal(0.5, result[1], 12);
        Assert.Equal(0.8, result[2], 12);
    }

    // --- B curves with parametric gamma ----------------------------------

    [Fact]
    public void Mab_B_curves_apply_gamma_to_each_channel()
    {
        ParametricCurveTagElement gamma22 = new(0, new[] { 2.2 });
        TagElement[] bCurves = { gamma22, gamma22, gamma22 };
        LutAToBTagElement tag = MakeMab(3, 3, bCurves: bCurves);
        MabPipeline p = new(tag);

        double[] result = p.Apply(new[] { 0.5, 0.25, 0.1 });
        Assert.Equal(Math.Pow(0.5, 2.2), result[0], 9);
        Assert.Equal(Math.Pow(0.25, 2.2), result[1], 9);
        Assert.Equal(Math.Pow(0.1, 2.2), result[2], 9);
    }

    // --- A curves + identity CLUT + B curves -----------------------------

    [Fact]
    public void Mab_with_A_clut_and_B_routes_through_each_block()
    {
        // 3D identity CLUT
        int g = 2;
        double[] values = new double[g * g * g * 3];
        int idx = 0;
        for (int i = 0; i < g; i++)
        for (int j = 0; j < g; j++)
        for (int k = 0; k < g; k++)
        {
            values[idx++] = i / 1.0;
            values[idx++] = j / 1.0;
            values[idx++] = k / 1.0;
        }
        LutClutData clut = new(new byte[] { 2, 2, 2 }, 2, values, 3);

        ParametricCurveTagElement scale2 = new(0, new[] { 1.0 });    // identity gamma
        TagElement[] aCurves = { scale2, scale2, scale2 };
        TagElement[] bCurves = Identity(3);

        LutAToBTagElement tag = MakeMab(3, 3, aCurves: aCurves, clut: clut, bCurves: bCurves);
        MabPipeline p = new(tag);

        double[] result = p.Apply(new[] { 0.3, 0.6, 0.9 });
        Assert.Equal(0.3, result[0], 9);
        Assert.Equal(0.6, result[1], 9);
        Assert.Equal(0.9, result[2], 9);
    }

    // --- Matrix + M curves -----------------------------------------------

    [Fact]
    public void Mab_with_matrix_and_M_curves()
    {
        // Output channels = 3, no CLUT/A curves; M curves identity; matrix scales by 2 and offsets by 0.1.
        double[] matrix = { 2, 0, 0,  0, 2, 0,  0, 0, 2,  0.1, 0.1, 0.1 };
        LutAToBTagElement tag = MakeMab(3, 3,
            mCurves: Identity(3),
            matrix: matrix,
            bCurves: Identity(3));
        MabPipeline p = new(tag);

        double[] result = p.Apply(new[] { 0.1, 0.2, 0.3 });
        Assert.Equal(0.3, result[0], 9); // 2*0.1 + 0.1
        Assert.Equal(0.5, result[1], 9);
        Assert.Equal(0.7, result[2], 9);
    }

    // --- Channel-count shape validation ----------------------------------

    [Fact]
    public void Mismatched_A_curve_count_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new MabPipeline(MakeMab(3, 3, aCurves: Identity(2))));
    }

    [Fact]
    public void Mismatched_B_curve_count_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new MabPipeline(MakeMab(3, 3, bCurves: Identity(4))));
    }

    [Fact]
    public void Matrix_without_M_curves_throws()
    {
        double[] matrix = { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 };
        Assert.Throws<ArgumentException>(() => new MabPipeline(MakeMab(3, 3, matrix: matrix)));
    }

    [Fact]
    public void Wrong_input_count_at_call_time_throws()
    {
        MabPipeline p = new(MakeMab(3, 3));
        Assert.Throws<ArgumentException>(() => p.Apply(new[] { 0.5, 0.5 }));
    }

    // --- mBA: minimal & full --------------------------------------------

    [Fact]
    public void Mba_with_only_B_curves_is_identity()
    {
        LutBToATagElement tag = new(3, 3, Identity(3), null, null, null, null);
        MbaPipeline p = new(tag);
        double[] result = p.Apply(new[] { 0.2, 0.5, 0.8 });
        Assert.Equal(0.2, result[0], 12);
        Assert.Equal(0.5, result[1], 12);
        Assert.Equal(0.8, result[2], 12);
    }

    [Fact]
    public void Mba_with_full_pipeline_round_trips_when_inverse_of_Mab()
    {
        // Build an mAB pipeline that scales each channel via B-curve gamma, then build an mBA
        // with the inverse gamma. Composition should reproduce the input.
        ParametricCurveTagElement gamma = new(0, new[] { 2.2 });
        ParametricCurveTagElement invGamma = new(0, new[] { 1.0 / 2.2 });

        MabPipeline forward = new(MakeMab(3, 3, bCurves: new[] { (TagElement)gamma, gamma, gamma }));
        MbaPipeline backward = new(new LutBToATagElement(
            3, 3,
            new[] { (TagElement)invGamma, invGamma, invGamma }, // B curves on mBA input side
            null, null, null, null));

        double[] start = { 0.3, 0.6, 0.9 };
        double[] mid = forward.Apply(start);
        double[] back = backward.Apply(mid);
        Assert.Equal(start[0], back[0], 9);
        Assert.Equal(start[1], back[1], 9);
        Assert.Equal(start[2], back[2], 9);
    }
}
