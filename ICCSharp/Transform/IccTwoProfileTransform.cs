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
    internal enum PcsBoundary
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
    private readonly (double X, double Y, double Z)? _absoluteScale;

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

        // Absolute colorimetric reproduces the source medium literally, so black point compensation is
        // meaningless there — and ISO 32000-2 8.6.5.9 makes it explicit for PDF: "If the current render
        // intent of an object is AbsColorimetric then the value of UseBlackPtComp shall be treated as
        // OFF." lcms disables BPC for absolute for the same reason.
        _bpc = blackPointCompensation && intent != RenderingIntent.AbsoluteColorimetric
            ? Eval.BlackPointCompensation.Build(DetectBlackPoint(source), DetectBlackPoint(destination))
            : null;

        // Absolute colorimetric reproduces the source's actual media white literally instead of
        // normalising it to the destination's (which is what relative colorimetric does). Precompute
        // the PCS scale srcMediaWhite / dstMediaWhite, applied per-pixel in Apply; other intents
        // leave it null and behave as relative.
        _absoluteScale = intent == RenderingIntent.AbsoluteColorimetric
            ? ComputeAbsoluteScale(source, destination)
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

        if (_absoluteScale is { } scale)
        {
            pcsXyz[0] *= scale.X;
            pcsXyz[1] *= scale.Y;
            pcsXyz[2] *= scale.Z;
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

    // ---- black point detection ------------------------------------------

    /// <summary>
    /// Determines the darkest colour <paramref name="p"/> can actually reproduce, for black point
    /// compensation.
    /// <para>
    /// The obvious source — the profile's <c>mediaBlackPoint</c> ('bkpt') tag — is NOT used, because in
    /// real-world profiles it is frequently wrong. The Agfa "Swop Standard" profile shipped with Windows
    /// declares a black point of L* 24.6 when its reachable black is L* 7.8; feeding that to BPC builds a
    /// scale that maps every colour darker than the declared point to a negative PCS value, which then
    /// clamps to zero. Shadow detail collapses: saturated darks such as a C+M violet lose two of three
    /// channels entirely.
    /// </para>
    /// <para>
    /// Instead the black point is DETECTED, which is what littleCMS does and what Adobe's output matches:
    /// round-trip PCS black through the profile — <c>Lab(0,0,0) → B2A(relative) → device → A2B(relative)
    /// → PCS</c> — and take where it lands. That asks the profile itself what its device black maps to.
    /// Matrix/TRC and gray/TRC profiles are exact at zero (their curves send 0 to 0), so they short
    /// circuit; only LUT-based profiles — in practice CMYK output profiles, the ones that matter here —
    /// need the round trip.
    /// </para>
    /// </summary>
    public static XyzNumber DetectBlackPoint(IccProfile p)
    {
        IColorTransform toDevice, toPcs;
        PcsBoundary fromBoundary, toBoundary;
        try
        {
            // Relative colorimetric on both legs: black point detection asks a media-relative question,
            // and the perceptual tables would fold in their own black handling.
            (toDevice, fromBoundary) = BuildFromPcs(p, RenderingIntent.RelativeColorimetric);
            (toPcs, toBoundary) = BuildToPcs(p, RenderingIntent.RelativeColorimetric);
        }
        catch (IccTransformException)
        {
            return new XyzNumber(0, 0, 0);   // no usable pipeline in one direction — treat black as zero
        }

        // A profile whose to-PCS leg is a matrix or gray TRC reaches exact zero; no round trip needed.
        if (toBoundary == PcsBoundary.AbsoluteXyz || fromBoundary == PcsBoundary.AbsoluteXyz)
            return new XyzNumber(0, 0, 0);

        if (toDevice.InputChannels != 3 || toPcs.OutputChannels != 3
            || toDevice.OutputChannels != toPcs.InputChannels)
            return new XyzNumber(0, 0, 0);

        try
        {
            // PCS black is Lab(0,0,0), which is XYZ(0,0,0) — encode it for this profile's boundary.
            Span<double> pcsBlack = [0, 0, 0];
            Span<double> encoded = stackalloc double[3];
            PcsCodec.Encode(pcsBlack, encoded, p.Header.ProfileConnectionSpace,
                legacyV2Lab: fromBoundary == PcsBoundary.LegacyEncoded);

            Span<double> device = stackalloc double[toDevice.OutputChannels];
            toDevice.Apply(encoded, device);
            for (var i = 0; i < device.Length; i++)
                device[i] = Math.Clamp(device[i], 0.0, 1.0);

            Span<double> back = stackalloc double[3];
            toPcs.Apply(device, back);

            Span<double> xyz = stackalloc double[3];
            PcsCodec.Decode(back, xyz, p.Header.ProfileConnectionSpace,
                legacyV2Lab: toBoundary == PcsBoundary.LegacyEncoded);

            // A malformed profile can round-trip to nonsense. Anything non-finite, negative, or lighter
            // than a quarter of media white is not a black point; fall back to zero, which degrades to
            // the pre-detection behaviour for a well-behaved profile rather than corrupting the scale.
            if (!IsSaneBlack(xyz))
                return new XyzNumber(0, 0, 0);

            return new XyzNumber(xyz[0], xyz[1], xyz[2]);
        }
        catch (IccTransformException)
        {
            return new XyzNumber(0, 0, 0);
        }
    }

    private static bool IsSaneBlack(ReadOnlySpan<double> xyz)
    {
        foreach (double v in xyz)
            if (double.IsNaN(v) || double.IsInfinity(v) || v < 0.0)
                return false;
        return xyz[1] <= 0.25;
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

    internal static (IColorTransform, PcsBoundary) BuildFromPcs(IccProfile p, RenderingIntent intent)
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

    // ---- absolute colorimetric -----------------------------------------

    /// <summary>Per-axis PCS scale that carries the source media white onto the destination's.</summary>
    private static (double X, double Y, double Z) ComputeAbsoluteScale(IccProfile src, IccProfile dst)
    {
        XyzNumber s = MediaWhite(src);
        XyzNumber d = MediaWhite(dst);
        return (d.X == 0 ? 1.0 : s.X / d.X,
                d.Y == 0 ? 1.0 : s.Y / d.Y,
                d.Z == 0 ? 1.0 : s.Z / d.Z);
    }

    /// <summary>
    /// The profile's actual media white. The 'chad' tag (when present) is the matrix that adapted
    /// the real illuminant to the D50 PCS, so its inverse applied to D50 recovers the actual white.
    /// Otherwise the 'wtpt' tag is the media white (v2 convention); failing that, D50.
    /// </summary>
    private static XyzNumber MediaWhite(IccProfile p)
    {
        var chad =
            p.GetTag<S15Fixed16ArrayTagElement>(IccTagSignatures.ChromaticAdaptation);
        if (chad is not null && chad.Values.Count >= 9)
        {
            var m = new Matrix3x3(
                chad.Values[0], chad.Values[1], chad.Values[2],
                chad.Values[3], chad.Values[4], chad.Values[5],
                chad.Values[6], chad.Values[7], chad.Values[8]);
            try
            {
                (double x, double y, double z) = m.Inverse().Transform(
                    StandardIlluminants.D50.X, StandardIlluminants.D50.Y, StandardIlluminants.D50.Z);
                return new XyzNumber(x, y, z);
            }
            catch (InvalidOperationException)
            {
                // Singular chad — fall back to wtpt/D50.
            }
        }

        return p.WhitePoint ?? StandardIlluminants.D50;
    }
}
