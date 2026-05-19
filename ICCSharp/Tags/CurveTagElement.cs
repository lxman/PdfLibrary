using System.Collections.Generic;
using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Tags;

/// <summary>
/// ICC.1:2010 §10.6 curveType — a tone reproduction curve given as:
///   • count == 0 → identity function (y = x)
///   • count == 1 → single u8Fixed8 gamma value (encoded in the one sample)
///   • count  > 1 → <paramref name="count"/> uniformly spaced uint16 sample points
///                  representing y over x ∈ [0, 1], y normalized over [0, 65535].
/// Interpretation is left to the curve evaluator (Layer 5); this layer preserves bytes.
/// </summary>
public sealed class CurveTagElement : TagElement
{
    public IReadOnlyList<ushort> Samples { get; }

    public CurveTagElement(IReadOnlyList<ushort> samples) : base(TagTypeSignatures.Curve)
    {
        Samples = samples;
    }

    /// <summary>True iff the curve is the identity function (count == 0).</summary>
    public bool IsIdentity => Samples.Count == 0;

    /// <summary>True iff the curve encodes a single u8Fixed8 gamma value (count == 1).</summary>
    public bool IsSingleGamma => Samples.Count == 1;

    /// <summary>The single gamma value, valid only when <see cref="IsSingleGamma"/> is true.</summary>
    public double SingleGamma => Samples[0] / 256.0;

    internal static CurveTagElement Parse(IccBinaryReader reader, int payloadBytes)
    {
        if (payloadBytes < 4)
            throw new IccParseException($"curveType payload {payloadBytes} bytes; need at least 4 for count.");

        uint count = reader.ReadUInt32();
        long bytesNeeded = (long)count * 2L;
        if (bytesNeeded > payloadBytes - 4)
            throw new IccParseException(
                $"curveType declares {count} samples ({bytesNeeded} bytes) but only {payloadBytes - 4} remain.");

        ushort[] samples = new ushort[count];
        for (uint i = 0; i < count; i++) samples[i] = reader.ReadUInt16();
        return new CurveTagElement(samples);
    }
}
