using System;
using ICCSharp.Eval;
using ICCSharp.IO;
using ICCSharp.Profile;
using ICCSharp.Transform;

namespace ICCSharp;

/// <summary>
/// Transforms CIE Lab values (L* 0..100, a*/b* signed — the PDF /Lab convention) directly through
/// a destination profile's PCS→device leg, i.e. treats the Lab values as already being in the PCS.
/// D50 is assumed (non-D50 white points are a documented conformance gap).
/// </summary>
public sealed class IccPcsLabTransform
{
    private readonly IColorTransform _fromPcs;
    private readonly IccTwoProfileTransform.PcsBoundary _boundary;
    private readonly IccSignature _destPcs;

    public int OutputChannels => _fromPcs.OutputChannels;

    private IccPcsLabTransform(IColorTransform fromPcs, IccTwoProfileTransform.PcsBoundary boundary, IccSignature destPcs)
    { _fromPcs = fromPcs; _boundary = boundary; _destPcs = destPcs; }

    public static IccPcsLabTransform Create(IccProfile destination, TransformOptions? options = null)
    {
        if (destination is null) throw new ArgumentNullException(nameof(destination));
        RenderingIntent intent = options?.Intent ?? RenderingIntent.RelativeColorimetric;
        (IColorTransform fromPcs, IccTwoProfileTransform.PcsBoundary boundary) =
            IccTwoProfileTransform.BuildFromPcs(destination, intent);
        if (fromPcs.InputChannels != 3)
            throw new IccTransformException($"Destination from-PCS leg must accept 3 channels, got {fromPcs.InputChannels}.");
        return new IccPcsLabTransform(fromPcs, boundary, destination.Header.ProfileConnectionSpace);
    }

    public void Apply(ReadOnlySpan<double> lab, Span<double> deviceOut)
    {
        XyzNumber xyz = LabXyzConverter.ToXyz(new LabNumber(lab[0], lab[1], lab[2]), StandardIlluminants.D50);
        Span<double> absXyz = stackalloc double[3] { xyz.X, xyz.Y, xyz.Z };
        Span<double> encoded = stackalloc double[3];
        switch (_boundary)
        {
            case IccTwoProfileTransform.PcsBoundary.AbsoluteXyz: absXyz.CopyTo(encoded); break;
            case IccTwoProfileTransform.PcsBoundary.ModernEncoded: PcsCodec.Encode(absXyz, encoded, _destPcs, legacyV2Lab: false); break;
            case IccTwoProfileTransform.PcsBoundary.LegacyEncoded: PcsCodec.Encode(absXyz, encoded, _destPcs, legacyV2Lab: true); break;
        }
        _fromPcs.Apply(encoded, deviceOut);
    }
}
