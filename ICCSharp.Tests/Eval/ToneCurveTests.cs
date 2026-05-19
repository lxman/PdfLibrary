using ICCSharp.Eval;
using ICCSharp.IO;
using ICCSharp.Tags;

namespace ICCSharp.Tests.Eval;

public class ToneCurveTests
{
    // --- SampledToneCurve: identity --------------------------------------

    [Fact]
    public void Sampled_identity_returns_x()
    {
        SampledToneCurve c = new(new CurveTagElement(Array.Empty<ushort>()));
        Assert.Equal(0.0, c.Evaluate(0.0));
        Assert.Equal(0.5, c.Evaluate(0.5));
        Assert.Equal(1.0, c.Evaluate(1.0));
        Assert.Equal(0.42, c.Evaluate(0.42));
    }

    [Fact]
    public void Sampled_identity_clamps_outside_zero_one()
    {
        SampledToneCurve c = new(new CurveTagElement(Array.Empty<ushort>()));
        Assert.Equal(0.0, c.Evaluate(-0.5));
        Assert.Equal(1.0, c.Evaluate(2.0));
    }

    // --- SampledToneCurve: single gamma ---------------------------------

    [Fact]
    public void Sampled_single_gamma_2_2_matches_analytical()
    {
        // gamma 2.2 → u8Fixed8 = 2.2 * 256 = 563.2 → 563 (0x0233 = 563); back to gamma = 563/256 = 2.19921875.
        ushort g = (ushort)Math.Round(2.2 * 256.0);
        SampledToneCurve c = new(new CurveTagElement(new ushort[] { g }));
        double gammaActual = g / 256.0;
        Assert.Equal(Math.Pow(0.5, gammaActual), c.Evaluate(0.5), 9);
        Assert.Equal(Math.Pow(0.25, gammaActual), c.Evaluate(0.25), 9);
    }

    [Fact]
    public void Sampled_single_gamma_inverse_undoes_forward()
    {
        ushort g = (ushort)Math.Round(2.2 * 256.0);
        SampledToneCurve c = new(new CurveTagElement(new ushort[] { g }));
        foreach (double x in new[] { 0.0, 0.1, 0.25, 0.5, 0.75, 0.9, 1.0 })
            Assert.Equal(x, c.EvaluateInverse(c.Evaluate(x)), 9);
    }

    // --- SampledToneCurve: lookup table ---------------------------------

    [Fact]
    public void Sampled_lookup_linear_ramp_is_near_identity()
    {
        // 256-entry linear ramp: samples[i] = i * 65535 / 255 = i * 257.
        ushort[] ramp = new ushort[256];
        for (int i = 0; i < 256; i++) ramp[i] = (ushort)(i * 257);
        SampledToneCurve c = new(new CurveTagElement(ramp));
        Assert.Equal(0.0, c.Evaluate(0.0), 6);
        Assert.Equal(0.5, c.Evaluate(0.5), 5);
        Assert.Equal(1.0, c.Evaluate(1.0), 6);
    }

    [Fact]
    public void Sampled_lookup_x_squared_curve()
    {
        // 257-entry x^2 LUT (i goes 0..256 — but ICC sample arrays count grid points, so use 257).
        // Actually ICC samples represent uniformly spaced points over [0,1]; n samples → n-1 intervals.
        int n = 257;
        ushort[] samples = new ushort[n];
        for (int i = 0; i < n; i++)
        {
            double x = i / (double)(n - 1);
            samples[i] = (ushort)Math.Round(x * x * 65535.0);
        }
        SampledToneCurve c = new(new CurveTagElement(samples));
        // At x=0.5, y ≈ 0.25 ± table quantization (~1/65535).
        Assert.Equal(0.25, c.Evaluate(0.5), 4);
        Assert.Equal(0.0,  c.Evaluate(0.0), 4);
        Assert.Equal(1.0,  c.Evaluate(1.0), 4);
        Assert.Equal(0.16, c.Evaluate(0.4), 3);
    }

    [Fact]
    public void Sampled_lookup_inverse_recovers_input()
    {
        int n = 257;
        ushort[] samples = new ushort[n];
        for (int i = 0; i < n; i++)
        {
            double x = i / (double)(n - 1);
            samples[i] = (ushort)Math.Round(x * x * 65535.0);
        }
        SampledToneCurve c = new(new CurveTagElement(samples));
        foreach (double x in new[] { 0.1, 0.3, 0.5, 0.7, 0.9 })
        {
            double y = c.Evaluate(x);
            double xBack = c.EvaluateInverse(y);
            Assert.Equal(x, xBack, 3);
        }
    }

    // --- ParametricToneCurve: type 0 ------------------------------------

    [Fact]
    public void Parametric_type0_is_simple_gamma()
    {
        ParametricCurveTagElement tag = new(0, new[] { 2.4 });
        ParametricToneCurve c = new(tag);
        Assert.Equal(Math.Pow(0.5, 2.4), c.Evaluate(0.5), 9);
        Assert.Equal(0.0, c.Evaluate(0.0), 9);
        Assert.Equal(1.0, c.Evaluate(1.0), 9);
    }

    [Fact]
    public void Parametric_type0_inverse_closed_form()
    {
        ParametricCurveTagElement tag = new(0, new[] { 2.4 });
        ParametricToneCurve c = new(tag);
        Assert.Equal(0.5, c.EvaluateInverse(Math.Pow(0.5, 2.4)), 9);
    }

    // --- ParametricToneCurve: sRGB (type 3) -----------------------------

    [Fact]
    public void Parametric_type3_matches_sRGB_reference_values()
    {
        // sRGB inverse EOTF parametric coefficients:
        //   g = 2.4
        //   a = 1/1.055 ≈ 0.94786...
        //   b = 0.055/1.055 ≈ 0.05214...
        //   c = 1/12.92 ≈ 0.07739...
        //   d = 0.04045
        // y = (a*x + b)^g for x >= d
        // y = c*x         for x <  d
        ParametricCurveTagElement tag = new(3, new[]
        {
            2.4,
            1.0 / 1.055,
            0.055 / 1.055,
            1.0 / 12.92,
            0.04045,
        });
        ParametricToneCurve c = new(tag);

        // Known sRGB→linear reference:
        //   sRGB 0.5    → linear ≈ 0.21404114
        //   sRGB 0.04   → linear ≈ 0.003095975 (linear segment: 0.04 / 12.92)
        //   sRGB 1.0    → linear = 1.0
        //   sRGB 0.0    → linear = 0.0
        Assert.Equal(0.21404114, c.Evaluate(0.5), 6);
        Assert.Equal(0.04 / 12.92, c.Evaluate(0.04), 6);
        Assert.Equal(1.0, c.Evaluate(1.0), 6);
        Assert.Equal(0.0, c.Evaluate(0.0), 6);
    }

    [Fact]
    public void Parametric_type3_inverse_round_trips()
    {
        ParametricCurveTagElement tag = new(3, new[]
        {
            2.4, 1.0 / 1.055, 0.055 / 1.055, 1.0 / 12.92, 0.04045,
        });
        ParametricToneCurve c = new(tag);

        foreach (double x in new[] { 0.0, 0.01, 0.04, 0.05, 0.2, 0.5, 0.75, 1.0 })
            Assert.Equal(x, c.EvaluateInverse(c.Evaluate(x)), 6);
    }

    // --- ParametricToneCurve: types 2 and 4 use bisection ---------------

    [Fact]
    public void Parametric_type2_inverse_via_bisection_round_trips()
    {
        // y = (a*x + b)^g + c
        ParametricCurveTagElement tag = new(2, new[] { 2.4, 0.9, 0.1, 0.05 });
        ParametricToneCurve c = new(tag);
        foreach (double x in new[] { 0.1, 0.3, 0.5, 0.7, 0.9 })
            Assert.Equal(x, c.EvaluateInverse(c.Evaluate(x)), 5);
    }

    // --- Factory --------------------------------------------------------

    [Fact]
    public void Factory_dispatches_to_sampled_or_parametric()
    {
        Assert.IsType<SampledToneCurve>(ToneCurve.FromTag(new CurveTagElement(new ushort[] { 256 })));
        Assert.IsType<ParametricToneCurve>(
            ToneCurve.FromTag(new ParametricCurveTagElement(0, new[] { 2.2 })));
    }

    [Fact]
    public void Factory_throws_for_non_curve_tag_types()
    {
        Assert.Throws<ArgumentException>(() => ToneCurve.FromTag(new SignatureTagElement(new IccSignature(0))));
    }
}
