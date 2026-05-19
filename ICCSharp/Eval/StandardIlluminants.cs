using ICCSharp.IO;

namespace ICCSharp.Eval;

/// <summary>
/// Reference white points (XYZ, normalized so Y = 1) for common standard illuminants. Values are
/// the CIE 1931 2° observer published whites (ICC profiles' PCS is always D50).
/// </summary>
public static class StandardIlluminants
{
    /// <summary>D50 — the ICC PCS white. Used by every v2/v4 ICC profile's profile-connection space.</summary>
    public static readonly XyzNumber D50 = new(0.96422, 1.00000, 0.82521);

    /// <summary>D65 — sRGB / Display P3 / Rec.2020 white.</summary>
    public static readonly XyzNumber D65 = new(0.95047, 1.00000, 1.08883);

    /// <summary>D55 — daylight photography white.</summary>
    public static readonly XyzNumber D55 = new(0.95682, 1.00000, 0.92149);

    /// <summary>D93 — older monitor calibration white.</summary>
    public static readonly XyzNumber D93 = new(0.95252, 1.00000, 1.22271);

    /// <summary>Illuminant A — tungsten/incandescent.</summary>
    public static readonly XyzNumber A   = new(1.09850, 1.00000, 0.35585);

    /// <summary>Equal-energy white (E).</summary>
    public static readonly XyzNumber E   = new(1.00000, 1.00000, 1.00000);
}
