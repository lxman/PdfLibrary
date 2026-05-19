using System;
using System.Collections.Generic;
using ICCSharp.Tags;

namespace ICCSharp.Eval;

/// <summary>
/// A fixed-length set of one-dimensional curves, one per channel. Compiling the parsed tag
/// elements up front lets pipeline application stay branch-free per channel.
/// </summary>
internal sealed class PerChannelCurves
{
    private readonly IToneCurve[] _curves;

    public int ChannelCount => _curves.Length;

    public PerChannelCurves(IReadOnlyList<TagElement> tags)
    {
        _curves = new IToneCurve[tags.Count];
        for (int i = 0; i < tags.Count; i++)
            _curves[i] = ToneCurve.FromTag(tags[i]);
    }

    /// <summary>Forward, in-place per channel. Buffer length must equal channel count.</summary>
    public void Apply(Span<double> buffer)
    {
        if (buffer.Length != _curves.Length)
            throw new ArgumentException(
                $"Buffer length {buffer.Length} does not match curve count {_curves.Length}.", nameof(buffer));
        for (int i = 0; i < _curves.Length; i++)
            buffer[i] = _curves[i].Evaluate(buffer[i]);
    }

    /// <summary>Inverse, in-place per channel.</summary>
    public void ApplyInverse(Span<double> buffer)
    {
        if (buffer.Length != _curves.Length)
            throw new ArgumentException(
                $"Buffer length {buffer.Length} does not match curve count {_curves.Length}.", nameof(buffer));
        for (int i = 0; i < _curves.Length; i++)
            buffer[i] = _curves[i].EvaluateInverse(buffer[i]);
    }
}
