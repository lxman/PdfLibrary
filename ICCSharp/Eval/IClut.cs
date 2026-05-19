using System;

namespace ICCSharp.Eval;

/// <summary>Common surface area for 3-D and N-D CLUTs so pipelines can hold one reference.</summary>
public interface IClut
{
    int InputChannels { get; }
    int OutputChannels { get; }
    void Apply(ReadOnlySpan<double> input, Span<double> output);
}
