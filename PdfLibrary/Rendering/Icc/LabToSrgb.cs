using ICCSharp;
using ICCSharp.Eval;
using ICCSharp.IO;
using ICCSharp.Profile;

namespace PdfLibrary.Rendering.Icc;

/// <summary>
/// Converts CIE Lab (with arbitrary reference white) to sRGB. Uses ICCSharp's
/// <see cref="LabXyzConverter"/> for the Lab→XYZ step and the built-in sRGB profile (lazily
/// cached) for the XYZ→sRGB step.
///
/// PDF Lab color spaces (§ 8.6.5.4) carry an explicit WhitePoint; many real PDFs use D50 rather
/// than the often-assumed D65, and accommodating either matters for accuracy.
/// </summary>
internal static class LabToSrgb
{
    private static volatile IccTransform? _xyzToSrgb;
    private static readonly object Lock = new();

    /// <summary>
    /// Converts <c>(L, a, b)</c> relative to <paramref name="referenceWhite"/> into device sRGB
    /// values in <c>[0, 1]</c>. Returns a 3-element array.
    /// </summary>
    public static double[] Convert(double l, double a, double b, XyzNumber referenceWhite)
    {
        XyzNumber xyz = LabXyzConverter.ToXyz(new LabNumber(l, a, b), referenceWhite);

        // Adapt XYZ from PDF-specified white to D50 (the ICC PCS white) so the sRGB profile's
        // colorant matrix (which is itself Bradford-adapted to D50) lands on the correct
        // sRGB primaries.
        if (!AreClose(referenceWhite, StandardIlluminants.D50))
        {
            Matrix3x3 toD50 = ChromaticAdaptation.ComputeMatrix(referenceWhite, StandardIlluminants.D50);
            (double X, double Y, double Z) = toD50.Transform(xyz.X, xyz.Y, xyz.Z);
            xyz = new XyzNumber(X, Y, Z);
        }

        IccTransform t = GetXyzToSrgbTransform();
        // The transform is sRGB → sRGB, so its "input" path is RGB. We need to feed it XYZ
        // directly. Bypass the IccTransform abstraction and apply the destination's matrix-TRC
        // inverse path manually using the built-in sRGB profile.
        return BuildSrgbFromXyz(xyz);
    }

    private static IccTransform GetXyzToSrgbTransform()
    {
        if (_xyzToSrgb is not null) return _xyzToSrgb;
        lock (Lock)
        {
            _xyzToSrgb ??= IccTransform.Create(BuiltInProfiles.Srgb, BuiltInProfiles.Srgb);
        }
        return _xyzToSrgb;
    }

    private static double[] BuildSrgbFromXyz(XyzNumber xyz)
    {
        // Use the sRGB profile colorants directly to invert PCS-XYZ → linear sRGB → display sRGB.
        IccProfile srgb = BuiltInProfiles.Srgb;
        XyzNumber rC = srgb.RedColorant   ?? new XyzNumber(0.43607, 0.22249, 0.01392);
        XyzNumber gC = srgb.GreenColorant ?? new XyzNumber(0.38515, 0.71687, 0.09708);
        XyzNumber bC = srgb.BlueColorant  ?? new XyzNumber(0.14307, 0.06061, 0.71410);
        Matrix3x3 inverse = Matrix3x3.FromColorants(rC, gC, bC).Inverse();

        (double linR, double linG, double linB) = inverse.Transform(xyz.X, xyz.Y, xyz.Z);

        // Apply inverse sRGB parametric curve to convert linear → display.
        IToneCurve toneCurve = BuildSrgbToneCurve();
        return
        [
            Clamp01(toneCurve.EvaluateInverse(Clamp01(linR))),
            Clamp01(toneCurve.EvaluateInverse(Clamp01(linG))),
            Clamp01(toneCurve.EvaluateInverse(Clamp01(linB))),
        ];
    }

    private static volatile IToneCurve? _srgbToneCurve;

    private static IToneCurve BuildSrgbToneCurve()
    {
        if (_srgbToneCurve is not null) return _srgbToneCurve;
        lock (Lock)
        {
            if (_srgbToneCurve is null)
            {
                IccProfile srgb = BuiltInProfiles.Srgb;
                if (srgb.RedTrc is null)
                    throw new InvalidOperationException("Built-in sRGB profile is missing rTRC tag.");
                _srgbToneCurve = ToneCurve.FromTag(srgb.RedTrc);
            }
        }
        return _srgbToneCurve;
    }

    private static bool AreClose(XyzNumber a, XyzNumber b)
        => Math.Abs(a.X - b.X) < 1e-4 && Math.Abs(a.Y - b.Y) < 1e-4 && Math.Abs(a.Z - b.Z) < 1e-4;

    private static double Clamp01(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
}
