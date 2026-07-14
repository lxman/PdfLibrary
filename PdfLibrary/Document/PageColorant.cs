using PdfLibrary.Structure;

namespace PdfLibrary.Document;

/// <summary>How a named Separation/DeviceN colorant relates to the process plates. Soft-Proof SP-1.</summary>
public enum ColorantKind { Spot, Process, All, None }

/// <summary>
/// One distinct named colorant used on a page (Soft-Proof SP-1), built by
/// <see cref="PdfDocument.GetPageColorants"/>. The plate-list + spot→display material for SP-2/SP-3.
/// </summary>
/// <param name="Name">The colorant name, verbatim from the PDF (e.g. "PANTONE 185 C").</param>
/// <param name="Kind">Process (Cyan/Magenta/Yellow/Black), Spot, All, or None.</param>
/// <param name="AlternateSpace">The tint transform's alternate space name (e.g. "DeviceCMYK", "Lab").</param>
/// <param name="TintRamp">256 samples, tint 0..1 → alternate-space colour; null when the tint transform
/// is missing/unbuildable.</param>
/// <param name="SolidSrgb">A representative sRGB solid (tint = 1) for UI swatches/labels.</param>
public sealed record PageColorant(
    string Name,
    ColorantKind Kind,
    string AlternateSpace,
    IReadOnlyList<double[]>? TintRamp,
    (byte R, byte G, byte B) SolidSrgb)
{
    internal static ColorantKind Classify(string name) => name switch
    {
        "Cyan" or "Magenta" or "Yellow" or "Black" => ColorantKind.Process,
        "All" => ColorantKind.All,
        "None" => ColorantKind.None,
        _ => ColorantKind.Spot,
    };
}
