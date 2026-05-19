using System;
using System.Collections.Generic;
using ICCSharp.Tags;

namespace ICCSharp.Eval;

/// <summary>
/// Evaluator for a <see cref="CurveTagElement"/>. Three forms per ICC.1:2010 §10.6:
///   • 0 samples → identity (y = x)
///   • 1 sample  → gamma function (y = x^γ; γ = sample / 256)
///   • N samples → uniformly spaced sample points with linear interpolation between them.
/// </summary>
public sealed class SampledToneCurve : ToneCurveBase
{
    private readonly ushort[] _samples;
    private readonly double _gamma;       // valid only when _samples.Length == 1
    private readonly Mode _mode;

    private enum Mode { Identity, Gamma, Lookup }

    public SampledToneCurve(CurveTagElement curve) : this(curve.Samples) { }

    internal SampledToneCurve(IReadOnlyList<ushort> samples)
    {
        _samples = new ushort[samples.Count];
        for (int i = 0; i < samples.Count; i++) _samples[i] = samples[i];

        if (_samples.Length == 0)
        {
            _mode = Mode.Identity;
            _gamma = 1.0;
        }
        else if (_samples.Length == 1)
        {
            _mode = Mode.Gamma;
            _gamma = _samples[0] / 256.0;
        }
        else
        {
            _mode = Mode.Lookup;
            _gamma = 1.0;
        }
    }

    public override double Evaluate(double x)
    {
        x = Clamp01(x);
        switch (_mode)
        {
            case Mode.Identity: return x;
            case Mode.Gamma:    return Math.Pow(x, _gamma);
            default:            return InterpLookup(x);
        }
    }

    public override double EvaluateInverse(double y)
    {
        y = Clamp01(y);
        switch (_mode)
        {
            case Mode.Identity: return y;
            case Mode.Gamma:
                if (_gamma == 0.0) return 0.0;
                return Math.Pow(y, 1.0 / _gamma);
            default:            return InverseLookup(y);
        }
    }

    private double InterpLookup(double x)
    {
        int n = _samples.Length;
        double pos = x * (n - 1);
        int i = (int)Math.Floor(pos);
        if (i >= n - 1) return _samples[n - 1] / 65535.0;
        double frac = pos - i;
        double lo = _samples[i];
        double hi = _samples[i + 1];
        double v = lo + (hi - lo) * frac;
        return v / 65535.0;
    }

    /// <summary>
    /// Binary search on the sample array for the inverse. Assumes the sampled curve is monotone
    /// non-decreasing; nothing in the spec mandates that, but every TRC produced by real-world
    /// profile creation tools is monotone.
    /// </summary>
    private double InverseLookup(double y)
    {
        int n = _samples.Length;
        double target = y * 65535.0;
        if (target <= _samples[0])     return 0.0;
        if (target >= _samples[n - 1]) return 1.0;

        // Find i such that samples[i] <= target < samples[i+1].
        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (_samples[mid] <= target) lo = mid;
            else hi = mid;
        }
        double sLo = _samples[lo];
        double sHi = _samples[lo + 1];
        if (sHi == sLo) return lo / (double)(n - 1);
        double frac = (target - sLo) / (sHi - sLo);
        return (lo + frac) / (n - 1);
    }
}
