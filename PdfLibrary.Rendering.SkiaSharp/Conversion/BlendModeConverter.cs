using PdfLibrary.Content;
using SkiaSharp;

namespace PdfLibrary.Rendering.SkiaSharp.Conversion;

/// <summary>
/// Utility for converting PDF blend mode names to SkiaSharp blend modes
/// </summary>
public static class BlendModeConverter
{
    /// <summary>
    /// Converts a PDF blend mode string to the corresponding SkiaSharp blend mode.
    /// PDF 1.4+ supports various blend modes for transparency operations.
    /// </summary>
    /// <param name="blendMode">The PDF blend mode name (e.g., "Normal", "Multiply", "Screen")</param>
    /// <returns>The corresponding SKBlendMode, defaulting to SrcOver for unknown modes</returns>
    public static SKBlendMode ConvertBlendMode(string blendMode)
    {
        return blendMode switch
        {
            "Normal" or "Compatible" => SKBlendMode.SrcOver,
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
            _ => SKBlendMode.SrcOver // Default to normal blend for unknown modes
        };
    }

    /// <summary>
    /// Determines the blend mode to use for a graphics state, considering both
    /// the explicit blend mode and overprint simulation.
    /// </summary>
    /// <param name="state">The PDF graphics state</param>
    /// <param name="useStrokeOverprint">If true, check stroke overprint; if false, check fill overprint</param>
    /// <returns>The SKBlendMode to use</returns>
    public static SKBlendMode GetBlendModeForState(PdfGraphicsState state, bool useStrokeOverprint = false)
    {
        // First check if an explicit blend mode is set
        if (!string.IsNullOrEmpty(state.BlendMode) && state.BlendMode != "Normal")
        {
            return ConvertBlendMode(state.BlendMode);
        }

        // Check for overprint simulation (overprint is approximated with Multiply blend)
        bool overprintEnabled = useStrokeOverprint ? state.StrokeOverprint : state.FillOverprint;
        return overprintEnabled
            ? SKBlendMode.Multiply
            // Default to normal blend (source over)
            : SKBlendMode.SrcOver;
    }
}
