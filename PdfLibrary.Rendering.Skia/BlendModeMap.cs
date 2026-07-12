using SkiaSharp;

namespace PdfLibrary.Rendering.Skia;

/// <summary>Maps a PDF /BM blend-mode name (ISO 32000-1 Table 136 separable + non-separable) to the
/// SkiaSharp equivalent. Unknown / "Normal" / "Compatible" / null fall back to SrcOver.</summary>
public static class BlendModeMap
{
    public static SKBlendMode FromPdf(string? blendMode) => blendMode switch
    {
        "Multiply" => SKBlendMode.Multiply,
        "Screen" => SKBlendMode.Screen,
        "Overlay" => SKBlendMode.Overlay,
        "Darken" => SKBlendMode.Darken,
        "Lighten" => SKBlendMode.Lighten,
        "ColorDodge" => SKBlendMode.ColorDodge,
        "ColorBurn" => SKBlendMode.ColorBurn,
        "HardLight" => SKBlendMode.HardLight,
        "SoftLight" => SKBlendMode.SoftLight,
        "Difference" => SKBlendMode.Difference,
        "Exclusion" => SKBlendMode.Exclusion,
        "Hue" => SKBlendMode.Hue,
        "Saturation" => SKBlendMode.Saturation,
        "Color" => SKBlendMode.Color,
        "Luminosity" => SKBlendMode.Luminosity,
        _ => SKBlendMode.SrcOver,
    };
}
