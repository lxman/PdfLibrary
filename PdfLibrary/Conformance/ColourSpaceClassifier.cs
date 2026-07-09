using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance;

/// <summary>
/// Classifies a PDF colour-space definition to the device colour family that governs it for conformance
/// (ISO 19005 6.2.4 / ISO 15930-7 colour). A Separation or DeviceN falls back to its alternate space, an
/// Indexed to its base, a Pattern to its underlying space; ICCBased, CalRGB, CalGray and Lab are
/// device-independent (they carry or reference their own colorimetry) and resolve to <see cref="OutputIntentColour.None"/>.
/// Resolves indirect references and guards against cyclic/nested definitions with a depth budget.
/// </summary>
internal static class ColourSpaceClassifier
{
    /// <summary>
    /// The device family a colour-space <paramref name="definition"/> ultimately resolves to, or
    /// <see cref="OutputIntentColour.None"/> when it is device-independent (ICC/Cal/Lab), a pattern with no
    /// underlying space, or unrecognised. <paramref name="definition"/> is a resolved colour-space object
    /// (a device name, or an array such as <c>[/Separation …]</c>) — a bare resource name must be looked up
    /// in the scope's /ColorSpace dictionary by the caller before being passed here.
    /// </summary>
    public static OutputIntentColour DeviceFamily(ConformanceContext context, PdfObject? definition) =>
        Classify(context, definition, depth: 0);

    private static OutputIntentColour Classify(ConformanceContext context, PdfObject? definition, int depth)
    {
        if (depth > 16)
            return OutputIntentColour.None; // runaway or cyclic alternate/base chain

        switch (context.Resolve(definition))
        {
            case PdfName name:
                return FamilyOfName(name.Value);

            case PdfArray { Count: > 0 } array:
                string? head = (context.Resolve(array[0]) as PdfName)?.Value;
                return head switch
                {
                    // Device-independent — governed by their own profile/colorimetry, never by the output intent.
                    "ICCBased" or "CalRGB" or "CalGray" or "Lab" => OutputIntentColour.None,
                    // Tint-based spaces fall back to their alternate space (element 2).
                    "Separation" or "DeviceN" when array.Count >= 3 => Classify(context, array[2], depth + 1),
                    // Indexed and Pattern govern through their base/underlying space (element 1).
                    "Indexed" or "Pattern" when array.Count >= 2 => Classify(context, array[1], depth + 1),
                    _ => FamilyOfName(head), // e.g. a single-element [/DeviceRGB] wrapper
                };

            default:
                return OutputIntentColour.None;
        }
    }

    private static OutputIntentColour FamilyOfName(string? name) => name switch
    {
        "DeviceGray" or "G" => OutputIntentColour.Gray,
        "DeviceRGB" or "RGB" => OutputIntentColour.Rgb,
        "DeviceCMYK" or "CMYK" => OutputIntentColour.Cmyk,
        _ => OutputIntentColour.None,
    };
}
