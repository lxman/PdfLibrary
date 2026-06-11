using System;
using ICCSharp.Eval;
using ICCSharp.IO;
using ICCSharp.Profile;
using ICCSharp.Tags;

namespace ICCSharp.Transform;

/// <summary>
/// Composes a source profile and destination profile into a single end-to-end color transform.
/// Internal connection space is absolute XYZ (Y = 1 for D50 white). The pipeline is:
///   input → [source pipeline] → [decode if CLUT-based] → [optional BPC]
///         → [encode if dest is CLUT-based] → [destination pipeline] → output
///
/// Supported tag families:
///   • Matrix/TRC (rXYZ + gXYZ + bXYZ + rTRC + gTRC + bTRC)
///   • lutAToBType ('mAB ') and lutBToAType ('mBA ') — modern v4 multi-stage pipelines (v4 Lab encoding)
///   • lut8Type ('mft1') and lut16Type ('mft2') — legacy v2 LUT tags (v2 Lab encoding)
/// </summary>
public sealed class IccTwoProfileTransform : IColorTransform
{
    /// <summary>How the source/destination pipeline represents PCS values at its boundary.</summary>
    private enum PcsBoundary
    {
        /// <summary>Pipeline emits or consumes absolute XYZ directly (matrix/TRC).</summary>
        AbsoluteXyz,
        /// <summary>v4 PCS encoding: XYZ as encoded [0,1]; Lab as L*/100 + (ab+128)/255.</summary>
        ModernEncoded,
        /// <summary>v2 PCS encoding (legacy LUT tags): same XYZ formula; Lab via 65280 scaling.</summary>
        LegacyEncoded,
    }

    public IccProfile Source { get; }
    public IccProfile Destination { get; }
    public RenderingIntent Intent { get; }
    public bool BlackPointCompensation { get; }

    private readonly IColorTransform _toPcs;
    private readonly IColorTransform _fromPcs;
    private readonly PcsBoundary _sourceBoundary;
    private readonly PcsBoundary _destBoundary;
    private readonly IccSignature _sourcePcs;
    private readonly IccSignature _destPcs;
    private readonly MatrixTransform? _bpc;

    public int InputChannels => _toPcs.InputChannels;
    public int OutputChannels => _fromPcs.OutputChannels;

    public IccTwoProfileTransform(
        IccProfile source,
        IccProfile destination,
        RenderingIntent intent = RenderingIntent.RelativeColorimetric,
        bool blackPointCompensation = false)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Destination = destination ?? throw new ArgumentNullException(nameof(destination));
        Intent = intent;
        BlackPointCompensation = blackPointCompensation;

        _sourcePcs = source.Header.ProfileConnectionSpace;
        _destPcs = destination.Header.ProfileConnectionSpace;

        (_toPcs, _sourceBoundary) = BuildToPcs(source, intent);
        (_fromPcs, _destBoundary) = BuildFromPcs(destination, intent);

        if (_toPcs.OutputChannels != 3 || _fromPcs.InputChannels != 3)
            throw new IccTransformException(
                $"Intermediate PCS must be 3 channels; got {_toPcs.OutputChannels} → {_fromPcs.InputChannels}.");

        _bpc = blackPointCompensation
            ? Eval.BlackPointCompensation.Build(
                source.BlackPoint ?? new XyzNumber(0, 0, 0),
                destination.BlackPoint ?? new XyzNumber(0, 0, 0))
            : null;
    }

    public void Apply(ReadOnlySpan<double> input, Span<double> output)
    {
        if (input.Length != InputChannels)
            throw new ArgumentException($"Expected {InputChannels} inputs; got {input.Length}.", nameof(input));
        if (output.Length < OutputChannels)
            throw new ArgumentException(
                $"Output buffer too short: need {OutputChannels}, got {output.Length}.", nameof(output));

        Span<double> srcOut = stackalloc double[3];
        Span<double> pcsXyz = stackalloc double[3];
        Span<double> dstIn = stackalloc double[3];

        _toPcs.Apply(input, srcOut);

        switch (_sourceBoundary)
        {
            case PcsBoundary.AbsoluteXyz:   srcOut.CopyTo(pcsXyz); break;
            case PcsBoundary.ModernEncoded: PcsCodec.Decode(srcOut, pcsXyz, _sourcePcs, legacyV2Lab: false); break;
            case PcsBoundary.LegacyEncoded: PcsCodec.Decode(srcOut, pcsXyz, _sourcePcs, legacyV2Lab: true); break;
        }

        if (_bpc is not null)
        {
            (double X, double Y, double Z) = _bpc.Transform(pcsXyz[0], pcsXyz[1], pcsXyz[2]);
            pcsXyz[0] = X; pcsXyz[1] = Y; pcsXyz[2] = Z;
        }

        switch (_destBoundary)
        {
            case PcsBoundary.AbsoluteXyz:   pcsXyz.CopyTo(dstIn); break;
            case PcsBoundary.ModernEncoded: PcsCodec.Encode(pcsXyz, dstIn, _destPcs, legacyV2Lab: false); break;
            case PcsBoundary.LegacyEncoded: PcsCodec.Encode(pcsXyz, dstIn, _destPcs, legacyV2Lab: true); break;
        }

        _fromPcs.Apply(dstIn, output);
    }

    // ---- pipeline builders ---------------------------------------------

    private static (IColorTransform, PcsBoundary) BuildToPcs(IccProfile p, RenderingIntent intent)
    {
        TagElement? a2b = SelectAToB(p, intent);
        if (a2b is LutAToBTagElement modernMab)
            return (new MabPipeline(modernMab), PcsBoundary.ModernEncoded);
        if (a2b is Lut8TagElement lut8)
            return (new LegacyLutPipeline(lut8), PcsBoundary.LegacyEncoded);
        if (a2b is Lut16TagElement lut16)
            return (new LegacyLutPipeline(lut16), PcsBoundary.LegacyEncoded);

        if (p.HasMatrixTrc)
            return (new MatrixTrcToPcs(p), PcsBoundary.AbsoluteXyz);

        if (p.HasGrayTrc)
            return (new GrayTrcToPcs(p), PcsBoundary.AbsoluteXyz);

        throw new IccTransformException(
            $"Source profile has no usable to-PCS path (no A2B0, no matrix/TRC, no gray TRC). Class={p.Header.Class}.");
    }

    private static (IColorTransform, PcsBoundary) BuildFromPcs(IccProfile p, RenderingIntent intent)
    {
        TagElement? b2a = SelectBToA(p, intent);
        if (b2a is LutBToATagElement modernMba)
            return (new MbaPipeline(modernMba), PcsBoundary.ModernEncoded);
        if (b2a is Lut8TagElement lut8)
            return (new LegacyLutPipeline(lut8), PcsBoundary.LegacyEncoded);
        if (b2a is Lut16TagElement lut16)
            return (new LegacyLutPipeline(lut16), PcsBoundary.LegacyEncoded);

        if (p.HasMatrixTrc)
            return (new MatrixTrcFromPcs(p), PcsBoundary.AbsoluteXyz);

        if (p.HasGrayTrc)
            return (new GrayTrcFromPcs(p), PcsBoundary.AbsoluteXyz);

        throw new IccTransformException(
            $"Destination profile has no usable from-PCS path (no B2A0, no matrix/TRC, no gray TRC). Class={p.Header.Class}.");
    }

    private static TagElement? SelectAToB(IccProfile p, RenderingIntent intent)
    {
        IccSignature primary = intent switch
        {
            RenderingIntent.Perceptual            => IccTagSignatures.AToB0,
            RenderingIntent.RelativeColorimetric  => IccTagSignatures.AToB1,
            RenderingIntent.Saturation            => IccTagSignatures.AToB2,
            RenderingIntent.AbsoluteColorimetric  => IccTagSignatures.AToB1,
            _ => IccTagSignatures.AToB0,
        };
        return p.GetTag(primary) ?? p.GetTag(IccTagSignatures.AToB0);
    }

    private static TagElement? SelectBToA(IccProfile p, RenderingIntent intent)
    {
        IccSignature primary = intent switch
        {
            RenderingIntent.Perceptual            => IccTagSignatures.BToA0,
            RenderingIntent.RelativeColorimetric  => IccTagSignatures.BToA1,
            RenderingIntent.Saturation            => IccTagSignatures.BToA2,
            RenderingIntent.AbsoluteColorimetric  => IccTagSignatures.BToA1,
            _ => IccTagSignatures.BToA0,
        };
        return p.GetTag(primary) ?? p.GetTag(IccTagSignatures.BToA0);
    }
}
