using ICCSharp.IO;

namespace ICCSharp.Tags;

/// <summary>
/// Type-signature constants for ICC tag element types (ICC.1:2010 §10).
/// Populated as each batch of tag types lands.
/// </summary>
public static class TagTypeSignatures
{
    // Batch 1 — numeric
    public static readonly IccSignature Xyz                 = IccSignature.FromAscii("XYZ ");
    public static readonly IccSignature S15Fixed16Array     = IccSignature.FromAscii("sf32");
    public static readonly IccSignature U16Fixed16Array     = IccSignature.FromAscii("uf32");
    public static readonly IccSignature SignatureType       = IccSignature.FromAscii("sig ");
    public static readonly IccSignature DateTimeType        = IccSignature.FromAscii("dtim");

    // Batch 2 — text
    public static readonly IccSignature Text                = IccSignature.FromAscii("text");
    public static readonly IccSignature TextDescription     = IccSignature.FromAscii("desc"); // v2 only
    public static readonly IccSignature MultiLocalizedUnicode = IccSignature.FromAscii("mluc");

    // Batch 3 — curves
    public static readonly IccSignature Curve               = IccSignature.FromAscii("curv");
    public static readonly IccSignature ParametricCurve     = IccSignature.FromAscii("para");

    // Batch 4 — legacy LUT
    public static readonly IccSignature Lut8                = IccSignature.FromAscii("mft1");
    public static readonly IccSignature Lut16               = IccSignature.FromAscii("mft2");

    // Batch 5 — modern LUT
    public static readonly IccSignature LutAToB             = IccSignature.FromAscii("mAB ");
    public static readonly IccSignature LutBToA             = IccSignature.FromAscii("mBA ");

    // Batch 6 — advanced
    public static readonly IccSignature Chromaticity        = IccSignature.FromAscii("chrm");
    public static readonly IccSignature Cicp                = IccSignature.FromAscii("cicp");
    public static readonly IccSignature Measurement         = IccSignature.FromAscii("meas");
    public static readonly IccSignature ViewingConditions   = IccSignature.FromAscii("view");
    public static readonly IccSignature ColorantTable       = IccSignature.FromAscii("clrt");
    public static readonly IccSignature ColorantOrder       = IccSignature.FromAscii("clro");
    public static readonly IccSignature NamedColor2         = IccSignature.FromAscii("ncl2");
}
