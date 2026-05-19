using System;
using ICCSharp.IO;

namespace ICCSharp.Eval;

/// <summary>
/// CIELAB ↔ CIEXYZ conversions per CIE 15:2004 (the formulation ICC.1:2010 §6.3.4 references).
/// Both forward and inverse use the standard piecewise function with the (6/29)³ threshold,
/// avoiding the cube-root singularity at zero.
/// </summary>
public static class LabXyzConverter
{
    private const double Delta = 6.0 / 29.0;
    private const double DeltaCubed = Delta * Delta * Delta;   // (6/29)³ ≈ 0.008856
    private const double KappaThird = 1.0 / 3.0 * (29.0 / 6.0) * (29.0 / 6.0); // (1/3)·(29/6)²
    private const double FourTwentyNinths = 4.0 / 29.0;

    /// <summary>Forward XYZ → Lab relative to <paramref name="reference"/> white (XYZ).</summary>
    public static LabNumber ToLab(XyzNumber xyz, XyzNumber reference)
    {
        double fx = F(xyz.X / reference.X);
        double fy = F(xyz.Y / reference.Y);
        double fz = F(xyz.Z / reference.Z);
        return new LabNumber(
            116.0 * fy - 16.0,
            500.0 * (fx - fy),
            200.0 * (fy - fz));
    }

    /// <summary>Inverse Lab → XYZ relative to <paramref name="reference"/> white (XYZ).</summary>
    public static XyzNumber ToXyz(LabNumber lab, XyzNumber reference)
    {
        double fy = (lab.L + 16.0) / 116.0;
        double fx = lab.A / 500.0 + fy;
        double fz = fy - lab.B / 200.0;
        return new XyzNumber(
            reference.X * FInv(fx),
            reference.Y * FInv(fy),
            reference.Z * FInv(fz));
    }

    private static double F(double t)
        => t > DeltaCubed ? Math.Cbrt(t) : KappaThird * t + FourTwentyNinths;

    private static double FInv(double t)
        => t > Delta ? t * t * t : 3.0 * Delta * Delta * (t - FourTwentyNinths);
}
