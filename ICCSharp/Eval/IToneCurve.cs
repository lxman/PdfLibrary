namespace ICCSharp.Eval;

/// <summary>
/// One-dimensional tone reproduction curve. Both the input and output are in [0, 1]; values outside
/// that range are clamped on input and may be saturated on output depending on the curve definition.
/// Implementations are assumed to be monotonically non-decreasing so that <see cref="EvaluateInverse"/>
/// is well-defined.
/// </summary>
public interface IToneCurve
{
    /// <summary>Forward evaluation: y = f(x).</summary>
    double Evaluate(double x);

    /// <summary>Inverse evaluation: x such that f(x) = y. Bisected when no closed form exists.</summary>
    double EvaluateInverse(double y);
}
