using ICCSharp.IO;

namespace ICCSharp.Profile;

/// <summary>
/// Well-known tag signatures from ICC.1:2010 §9. These identify the *meaning* of a tag in the
/// profile (where it lives, what it carries) — distinct from the type signatures inside the tag
/// data which identify the on-disk encoding.
/// </summary>
public static class IccTagSignatures
{
    // Description & metadata
    public static readonly IccSignature ProfileDescription = IccSignature.FromAscii("desc");
    public static readonly IccSignature Copyright          = IccSignature.FromAscii("cprt");
    public static readonly IccSignature DeviceMfgDesc      = IccSignature.FromAscii("dmnd");
    public static readonly IccSignature DeviceModelDesc    = IccSignature.FromAscii("dmdd");
    public static readonly IccSignature ViewingCondDesc    = IccSignature.FromAscii("vued");
    public static readonly IccSignature CharTarget         = IccSignature.FromAscii("targ");
    public static readonly IccSignature Technology         = IccSignature.FromAscii("tech");

    // White & black points
    public static readonly IccSignature MediaWhitePoint    = IccSignature.FromAscii("wtpt");
    public static readonly IccSignature MediaBlackPoint    = IccSignature.FromAscii("bkpt");
    public static readonly IccSignature Luminance          = IccSignature.FromAscii("lumi");

    // Three-component matrix/TRC (v2 RGB-display, v4 also)
    public static readonly IccSignature RedColorant        = IccSignature.FromAscii("rXYZ");
    public static readonly IccSignature GreenColorant      = IccSignature.FromAscii("gXYZ");
    public static readonly IccSignature BlueColorant       = IccSignature.FromAscii("bXYZ");
    public static readonly IccSignature RedTrc             = IccSignature.FromAscii("rTRC");
    public static readonly IccSignature GreenTrc           = IccSignature.FromAscii("gTRC");
    public static readonly IccSignature BlueTrc            = IccSignature.FromAscii("bTRC");
    public static readonly IccSignature GrayTrc            = IccSignature.FromAscii("kTRC");

    // Lookup-table tags (one per rendering intent)
    public static readonly IccSignature AToB0              = IccSignature.FromAscii("A2B0");
    public static readonly IccSignature AToB1              = IccSignature.FromAscii("A2B1");
    public static readonly IccSignature AToB2              = IccSignature.FromAscii("A2B2");
    public static readonly IccSignature BToA0              = IccSignature.FromAscii("B2A0");
    public static readonly IccSignature BToA1              = IccSignature.FromAscii("B2A1");
    public static readonly IccSignature BToA2              = IccSignature.FromAscii("B2A2");
    public static readonly IccSignature Gamut              = IccSignature.FromAscii("gamt");
    public static readonly IccSignature Preview0           = IccSignature.FromAscii("pre0");
    public static readonly IccSignature Preview1           = IccSignature.FromAscii("pre1");
    public static readonly IccSignature Preview2           = IccSignature.FromAscii("pre2");

    // v4 D-style float pipelines (output-referred floating-point)
    public static readonly IccSignature DToB0              = IccSignature.FromAscii("D2B0");
    public static readonly IccSignature DToB1              = IccSignature.FromAscii("D2B1");
    public static readonly IccSignature DToB2              = IccSignature.FromAscii("D2B2");
    public static readonly IccSignature DToB3              = IccSignature.FromAscii("D2B3");
    public static readonly IccSignature BToD0              = IccSignature.FromAscii("B2D0");
    public static readonly IccSignature BToD1              = IccSignature.FromAscii("B2D1");
    public static readonly IccSignature BToD2              = IccSignature.FromAscii("B2D2");
    public static readonly IccSignature BToD3              = IccSignature.FromAscii("B2D3");

    // Auxiliary
    public static readonly IccSignature Chromaticity       = IccSignature.FromAscii("chrm");
    public static readonly IccSignature ColorantOrder      = IccSignature.FromAscii("clro");
    public static readonly IccSignature ColorantTable      = IccSignature.FromAscii("clrt");
    public static readonly IccSignature ColorantTableOut   = IccSignature.FromAscii("clot");
    public static readonly IccSignature Measurement        = IccSignature.FromAscii("meas");
    public static readonly IccSignature NamedColor2        = IccSignature.FromAscii("ncl2");
    public static readonly IccSignature ViewingConditions  = IccSignature.FromAscii("view");
    public static readonly IccSignature Cicp               = IccSignature.FromAscii("cicp");
    public static readonly IccSignature ChromaticAdaptation= IccSignature.FromAscii("chad");

    // Rendering-intent gamut / perceptual reference
    public static readonly IccSignature PerceptualRenderingIntentGamut = IccSignature.FromAscii("rig0");
    public static readonly IccSignature SaturationRenderingIntentGamut = IccSignature.FromAscii("rig2");
}
