using System;

namespace ICCSharp.Eval;

/// <summary>
/// Generic N-channel color transform. The pipeline layer composes these out of curves, CLUTs, and
/// matrices; the public CMM API (Layer 12) ultimately exposes the same shape.
/// </summary>
public interface IColorTransform
{
    int InputChannels { get; }
    int OutputChannels { get; }

    /// <summary>Forward evaluation. Input and output are normalized to [0, 1].</summary>
    void Apply(ReadOnlySpan<double> input, Span<double> output);
}

public static class ColorTransformExtensions
{
    /// <summary>Allocating overload for convenience; calls the Span-based <see cref="IColorTransform.Apply"/>.</summary>
    public static double[] Apply(this IColorTransform t, ReadOnlySpan<double> input)
    {
        var result = new double[t.OutputChannels];
        t.Apply(input, result);
        return result;
    }
}
