using System;
using ICCSharp.Tags;

namespace ICCSharp.Eval;

/// <summary>
/// Evaluator for a <see cref="ParametricCurveTagElement"/>. Closed-form forward for all spec function
/// types (0–4); closed-form inverse for types 0, 1, 3 (the common ones used by sRGB / L*); bisection
/// fallback for types 2 and 4.
///
/// Function definitions (ICC.1:2010 §10.16):
///   0  y = x^g
///   1  y = (a·x + b)^g     for x ≥ -b/a; else 0
///   2  y = (a·x + b)^g + c for x ≥ -b/a; else c
///   3  y = (a·x + b)^g     for x ≥ d;   else c·x
///   4  y = (a·x + b)^g + e for x ≥ d;   else c·x + f
/// </summary>
public sealed class ParametricToneCurve : ToneCurveBase
{
    public ushort FunctionType { get; }
    private readonly double _g, _a, _b, _c, _d, _e, _f;

    public ParametricToneCurve(ParametricCurveTagElement tag)
    {
        FunctionType = tag.FunctionType;
        // Pull params with safe defaults — the tag parser already validated the count, but reading
        // beyond what a function type uses just leaves zeros.
        var p = new double[7];
        for (var i = 0; i < tag.Parameters.Count && i < 7; i++) p[i] = tag.Parameters[i];
        _g = p[0]; _a = p[1]; _b = p[2]; _c = p[3]; _d = p[4]; _e = p[5]; _f = p[6];
    }

    public override double Evaluate(double x)
    {
        x = Clamp01(x);
        switch (FunctionType)
        {
            case 0:
                return SafePow(x, _g);
            case 1:
                {
                    double threshold = _a != 0 ? -_b / _a : 0;
                    return x >= threshold ? SafePow(_a * x + _b, _g) : 0.0;
                }
            case 2:
                {
                    double threshold = _a != 0 ? -_b / _a : 0;
                    return x >= threshold ? SafePow(_a * x + _b, _g) + _c : _c;
                }
            case 3:
                return x >= _d ? SafePow(_a * x + _b, _g) : _c * x;
            case 4:
                return x >= _d ? SafePow(_a * x + _b, _g) + _e : _c * x + _f;
            default:
                return x;
        }
    }

    public override double EvaluateInverse(double y)
    {
        y = Clamp01(y);
        switch (FunctionType)
        {
            case 0:
                if (_g == 0) return 0;
                return SafePow(y, 1.0 / _g);
            case 1:
                if (_a == 0 || _g == 0) return 0;
                if (y <= 0) return -_b / _a;
                return (SafePow(y, 1.0 / _g) - _b) / _a;
            case 3:
                {
                    if (_g == 0) return 0;
                    // y_break = (a·d + b)^g
                    double yBreak = SafePow(_a * _d + _b, _g);
                    if (y >= yBreak)
                        return _a != 0 ? (SafePow(y, 1.0 / _g) - _b) / _a : 0;
                    return _c != 0 ? y / _c : 0;
                }
            // Types 2 and 4 have closed-form inverses too but the break-point logic is fiddly;
            // bisection is simpler and the precision loss is well below the s15Fixed16 floor.
            case 2:
            case 4:
                return Bisect(y);
            default:
                return y;
        }
    }

    /// <summary>Power that returns 0 for non-positive base (the spec extends the curve as constant left of the break).</summary>
    private static double SafePow(double b, double e) => b <= 0 ? 0 : Math.Pow(b, e);
}
