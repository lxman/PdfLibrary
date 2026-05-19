using System;
using ICCSharp.Tags;

namespace ICCSharp.Eval;

/// <summary>Factory: wrap a parsed tag element in the appropriate <see cref="IToneCurve"/>.</summary>
public static class ToneCurve
{
    public static IToneCurve FromTag(TagElement element)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        return element switch
        {
            CurveTagElement c => new SampledToneCurve(c),
            ParametricCurveTagElement p => new ParametricToneCurve(p),
            _ => throw new ArgumentException(
                $"Tag element type {element.GetType().Name} is not a tone curve.", nameof(element)),
        };
    }
}
