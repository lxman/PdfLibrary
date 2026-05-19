using System;
using ICCSharp.Eval;
using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Transform;

/// <summary>
/// Encodes / decodes the boundary between a CLUT-bearing pipeline (which emits or consumes raw
/// normalized [0,1] values) and the internal PCS convention used by the two-profile transform.
/// Internal PCS = absolute XYZ (Y = 1.0 for D50 white). Lab PCS profiles get round-tripped via
/// <see cref="LabXyzConverter"/>.
///
/// Lab encoding differs between modern v4 mAB/mBA tags and legacy v2 lut8/lut16 tags
/// (ICC.1:2010 §6.3.4.2 and Annex A.3). The <c>legacyV2Lab</c> flag selects between them.
/// XYZ encoding is identical for v2 and v4 (X_normalized = X_real / IccMaxXyz).
/// </summary>
internal static class PcsCodec
{
    public static bool IsLab(IccSignature pcs) => pcs == ColorSpaceSignatures.Lab;

    /// <summary>Decode raw CLUT-normalized values to absolute XYZ.</summary>
    public static void Decode(
        ReadOnlySpan<double> raw, Span<double> absoluteXyz, IccSignature pcs, bool legacyV2Lab = false)
    {
        if (IsLab(pcs))
        {
            double L, a, b;
            if (legacyV2Lab)
            {
                // v2 Lab16 encoding: L_storage = L*/100 * 65280; a,b storage = (val+128)*256.
                L = raw[0] * 100.0 * 65535.0 / 65280.0;
                a = raw[1] * 65535.0 / 256.0 - 128.0;
                b = raw[2] * 65535.0 / 256.0 - 128.0;
            }
            else
            {
                // v4 PCSLab encoding: L*/100, (a+128)/255, (b+128)/255 in [0,1].
                L = raw[0] * 100.0;
                a = raw[1] * 255.0 - 128.0;
                b = raw[2] * 255.0 - 128.0;
            }
            XyzNumber xyz = LabXyzConverter.ToXyz(new LabNumber(L, a, b), StandardIlluminants.D50);
            absoluteXyz[0] = xyz.X;
            absoluteXyz[1] = xyz.Y;
            absoluteXyz[2] = xyz.Z;
        }
        else
        {
            absoluteXyz[0] = raw[0] * IccConstants.IccMaxXyz;
            absoluteXyz[1] = raw[1] * IccConstants.IccMaxXyz;
            absoluteXyz[2] = raw[2] * IccConstants.IccMaxXyz;
        }
    }

    /// <summary>Encode absolute XYZ to raw CLUT-normalized values.</summary>
    public static void Encode(
        ReadOnlySpan<double> absoluteXyz, Span<double> raw, IccSignature pcs, bool legacyV2Lab = false)
    {
        if (IsLab(pcs))
        {
            LabNumber lab = LabXyzConverter.ToLab(
                new XyzNumber(absoluteXyz[0], absoluteXyz[1], absoluteXyz[2]),
                StandardIlluminants.D50);
            if (legacyV2Lab)
            {
                raw[0] = lab.L / 100.0 * 65280.0 / 65535.0;
                raw[1] = (lab.A + 128.0) * 256.0 / 65535.0;
                raw[2] = (lab.B + 128.0) * 256.0 / 65535.0;
            }
            else
            {
                raw[0] = lab.L / 100.0;
                raw[1] = (lab.A + 128.0) / 255.0;
                raw[2] = (lab.B + 128.0) / 255.0;
            }
        }
        else
        {
            raw[0] = absoluteXyz[0] / IccConstants.IccMaxXyz;
            raw[1] = absoluteXyz[1] / IccConstants.IccMaxXyz;
            raw[2] = absoluteXyz[2] / IccConstants.IccMaxXyz;
        }
    }
}
