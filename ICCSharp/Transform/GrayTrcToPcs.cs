using System;
using ICCSharp.Eval;
using ICCSharp.IO;
using ICCSharp.Profile;
using ICCSharp.Tags;

namespace ICCSharp.Transform;

/// <summary>
/// Source-side pipeline for monochrome (GRAY) profiles: the single kTRC maps device gray to a
/// position along the neutral axis, scaled by the D50 PCS white. Output is PCS-XYZ (D50).
///
/// Anchoring to D50 rather than the profile's wtpt is the relative-colorimetric convention —
/// media white coincides with PCS white, so gray inputs yield exactly neutral PCS values. (Many
/// real gray profiles store an un-adapted media white, e.g. D65, in wtpt; using it here would
/// tint every gray. Recovering the absolute media white for absolute-colorimetric intent needs
/// the 'chad' tag, which this pipeline does not consume.)
/// </summary>
internal sealed class GrayTrcToPcs : IColorTransform
{
    private readonly IToneCurve _trc;

    public int InputChannels => 1;
    public int OutputChannels => 3;

    public GrayTrcToPcs(IccProfile p)
    {
        TagElement k = p.GrayTrc ?? throw new IccTransformException("Gray profile missing kTRC.");
        _trc = ToneCurve.FromTag(k);
    }

    public void Apply(ReadOnlySpan<double> input, Span<double> output)
    {
        double y = _trc.Evaluate(input[0]);
        output[0] = y * StandardIlluminants.D50.X;
        output[1] = y * StandardIlluminants.D50.Y;
        output[2] = y * StandardIlluminants.D50.Z;
    }
}
