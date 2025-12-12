using Logging;
using SkiaSharp;
using Wacton.Unicolour;

namespace PdfLibrary.Rendering.SkiaSharp.Conversion;

/// <summary>
/// Converts PDF color spaces to SkiaSharp SKColor values.
/// Handles DeviceGray, DeviceRGB, DeviceCMYK, Lab, and fallback conversions.
/// </summary>
internal static class ColorConverter
{
    /// <summary>
    /// Convert PDF color components and color space to SKColor.
    /// </summary>
    /// <param name="colorComponents">Color component values (0.0 to 1.0)</param>
    /// <param name="colorSpace">PDF color space name (e.g., "DeviceRGB", "DeviceCMYK")</param>
    /// <returns>SKColor representation of the color</returns>
    public static SKColor ConvertColor(List<double> colorComponents, string colorSpace)
    {
        if (colorComponents.Count == 0)
            return SKColors.Black;

        switch (colorSpace)
        {
            case "DeviceGray":
            case "CalGray":
                {
                    var gray = (byte)(colorComponents[0] * 255);
                    return new SKColor(gray, gray, gray);
                }
            case "DeviceRGB":
            case "CalRGB":
                if (colorComponents.Count >= 3)
                {
                    // Clamp to [0, 1] before converting to byte to prevent overflow
                    var r = (byte)(Math.Clamp(colorComponents[0], 0.0, 1.0) * 255);
                    var g = (byte)(Math.Clamp(colorComponents[1], 0.0, 1.0) * 255);
                    var b = (byte)(Math.Clamp(colorComponents[2], 0.0, 1.0) * 255);
                    return new SKColor(r, g, b);
                }
                break;
            case "DeviceCMYK":
                if (colorComponents.Count >= 4)
                {
                    double c = colorComponents[0];
                    double m = colorComponents[1];
                    double y = colorComponents[2];
                    double k = colorComponents[3];

                    // Manual CMYK→RGB conversion (naive, without ICC profile)
                    // Unicolour only supports CMYK through ICC profiles, which DeviceCMYK doesn't provide
                    // This is the standard naive conversion appropriate for DeviceCMYK
                    var r = (byte)((1 - Math.Min(1.0, c * (1 - k) + k)) * 255);
                    var g = (byte)((1 - Math.Min(1.0, m * (1 - k) + k)) * 255);
                    var b = (byte)((1 - Math.Min(1.0, y * (1 - k) + k)) * 255);

                    // Debug logging for CMYK conversion
                    PdfLogger.Log(LogCategory.Graphics,
                        $"CMYK→RGB: CMYK=[{c:F2},{m:F2},{y:F2},{k:F2}] → RGB=({r},{g},{b})");

                    return new SKColor(r, g, b);
                }
                break;
            case "Lab":
                if (colorComponents.Count >= 3)
                {
                    // Lab color space: L* (0-100), a* (-128 to 127), b* (-128 to 127)
                    double L = colorComponents[0];
                    double a = colorComponents[1];
                    double b = colorComponents[2];

                    // Use Unicolour for Lab→RGB conversion
                    var unicolour = new Unicolour(ColourSpace.Lab, L, a, b);
                    Rgb rgb = unicolour.Rgb;

                    var rByte = (byte)(Math.Clamp(rgb.R, 0.0, 1.0) * 255);
                    var gByte = (byte)(Math.Clamp(rgb.G, 0.0, 1.0) * 255);
                    var bByte = (byte)(Math.Clamp(rgb.B, 0.0, 1.0) * 255);

                    // Debug logging for Lab conversion
                    PdfLogger.Log(LogCategory.Graphics,
                        $"Lab→RGB: Lab=[{L:F2},{a:F2},{b:F2}] → RGB=({rByte},{gByte},{bByte})");

                    return new SKColor(rByte, gByte, bByte);
                }
                break;
            default:
                switch (colorComponents.Count)
                {
                    // For named/unknown color spaces, try to interpret based on component count
                    // This is a fallback - proper implementation would resolve the named color space
                    case >= 4:
                    {
                        // Treat as CMYK
                        double c = colorComponents[0];
                        double m = colorComponents[1];
                        double y = colorComponents[2];
                        double k = colorComponents[3];
                        var r = (byte)((1 - c) * (1 - k) * 255);
                        var g = (byte)((1 - m) * (1 - k) * 255);
                        var b = (byte)((1 - y) * (1 - k) * 255);
                        return new SKColor(r, g, b);
                    }
                    case >= 3:
                    {
                        // Treat as RGB
                        var r = (byte)(colorComponents[0] * 255);
                        var g = (byte)(colorComponents[1] * 255);
                        var b = (byte)(colorComponents[2] * 255);
                        return new SKColor(r, g, b);
                    }
                    case >= 1:
                    {
                        // Treat as grayscale
                        var gray = (byte)(colorComponents[0] * 255);
                        return new SKColor(gray, gray, gray);
                    }
                }

                break;
        }

        return SKColors.Black;
    }

    /// <summary>
    /// Applies alpha value from graphics state to a color.
    /// Alpha is specified in PDF as a value from 0.0 (transparent) to 1.0 (opaque).
    /// </summary>
    /// <param name="color">Base color</param>
    /// <param name="alpha">Alpha value (0.0 to 1.0)</param>
    /// <returns>Color with alpha applied</returns>
    public static SKColor ApplyAlpha(SKColor color, double alpha)
    {
        if (alpha >= 1.0)
            return color;

        var alphaByte = (byte)(Math.Clamp(alpha, 0.0, 1.0) * 255);
        return color.WithAlpha(alphaByte);
    }
}
