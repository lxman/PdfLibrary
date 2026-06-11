namespace ICCSharp.Eval;

/// <summary>Common machinery for tone curves; supplies bisection-based inverse fallback.</summary>
public abstract class ToneCurveBase : IToneCurve
{
    public abstract double Evaluate(double x);

    public virtual double EvaluateInverse(double y) => Bisect(y);

    /// <summary>
    /// Standard bisection on [0, 1] assuming the curve is monotone non-decreasing. 40 iterations
    /// gives ~1e-12 precision on [0,1] — well below the s15Fixed16 noise floor we're working with.
    /// </summary>
    protected double Bisect(double target)
    {
        if (target <= Evaluate(0.0)) return 0.0;
        if (target >= Evaluate(1.0)) return 1.0;
        double lo = 0.0, hi = 1.0;
        for (int i = 0; i < 40; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (Evaluate(mid) < target) lo = mid;
            else hi = mid;
        }
        return 0.5 * (lo + hi);
    }

    protected static double Clamp01(double x) => x < 0.0 ? 0.0 : (x > 1.0 ? 1.0 : x);
}
