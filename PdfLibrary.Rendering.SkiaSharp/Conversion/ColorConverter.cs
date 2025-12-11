using Logging;
using SkiaSharp;

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

                    // Improved CMYK to RGB conversion
                    // Adobe uses ICC profiles, but this provides a reasonable approximation
                    // The key insight is that we convert to CMY first, then apply black
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

                    // Default white point (D65 if not specified)
                    double Xn = 0.9642, Yn = 1.0, Zn = 0.8249;

                    // Convert Lab to XYZ
                    double fy = (L + 16) / 116.0;
                    double fx = fy + (a / 500.0);
                    double fz = fy - (b / 200.0);

                    double xr = fx * fx * fx;
                    if (xr <= 0.008856) xr = (fx - 16.0 / 116.0) / 7.787;

                    double yr = fy * fy * fy;
                    if (yr <= 0.008856) yr = (fy - 16.0 / 116.0) / 7.787;

                    double zr = fz * fz * fz;
                    if (zr <= 0.008856) zr = (fz - 16.0 / 116.0) / 7.787;

                    double X = xr * Xn;
                    double Y = yr * Yn;
                    double Z = zr * Zn;

                    // Convert XYZ to sRGB (using standard D65 matrix)
                    double rLinear =  3.2406 * X - 1.5372 * Y - 0.4986 * Z;
                    double gLinear = -0.9689 * X + 1.8758 * Y + 0.0415 * Z;
                    double bLinear =  0.0557 * X - 0.2040 * Y + 1.0570 * Z;

                    // Apply gamma correction for sRGB
                    var gamma = (double v) => v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1.0 / 2.4) - 0.055;
                    double rSrgb = gamma(rLinear);
                    double gSrgb = gamma(gLinear);
                    double bSrgb = gamma(bLinear);

                    // Clamp to [0, 1] and convert to byte
                    var clamp = (double v) => Math.Max(0, Math.Min(1, v));
                    var rByte = (byte)(clamp(rSrgb) * 255);
                    var gByte = (byte)(clamp(gSrgb) * 255);
                    var bByte = (byte)(clamp(bSrgb) * 255);

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
