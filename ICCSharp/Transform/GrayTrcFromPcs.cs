using System;
using ICCSharp.Eval;
using ICCSharp.Profile;
using ICCSharp.Tags;

namespace ICCSharp.Transform;

/// <summary>
/// Destination-side pipeline for monochrome (GRAY) profiles: projects PCS-XYZ onto the neutral
/// axis by taking the D50-relative luminance (Y) and inverting the kTRC. Input is PCS-XYZ (D50);
/// output is a single device-gray channel. Counterpart to <see cref="GrayTrcToPcs"/>.
/// </summary>
internal sealed class GrayTrcFromPcs : IColorTransform
{
    private readonly IToneCurve _trc;

    public int InputChannels => 3;
    public int OutputChannels => 1;

    public GrayTrcFromPcs(IccProfile p)
    {
        TagElement k = p.GrayTrc ?? throw new IccTransformException("Gray profile missing kTRC.");
        _trc = ToneCurve.FromTag(k);
    }

    public void Apply(ReadOnlySpan<double> input, Span<double> output)
    {
        // Luminance relative to the D50 PCS white (D50.Y == 1, kept explicit for symmetry with
        // GrayTrcToPcs). Chromaticity is discarded — a gray device reproduces only the neutral axis.
        double y = input[1] / StandardIlluminants.D50.Y;
        output[0] = _trc.EvaluateInverse(y);
    }
}
