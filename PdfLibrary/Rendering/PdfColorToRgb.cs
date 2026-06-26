using Wacton.Unicolour;

namespace PdfLibrary.Rendering;

/// <summary>
/// SkiaSharp-free resolution of a PDF device-color component list to an RGB triple, for render
/// targets. Mirrors the SkiaSharp ColorConverter math; Lab uses Wacton.Unicolour (a core dep).
/// Public so third-party IRenderTarget authors (in other assemblies) can reuse it.
/// </summary>
public static class PdfColorToRgb
{
    public static (byte R, byte G, byte B) ToRgb(IReadOnlyList<double> components, string? colorSpace)
    {
        if (components is null || components.Count == 0) return (0, 0, 0);

        switch (colorSpace)
        {
            case "DeviceGray" or "CalGray" when components.Count >= 1:
            {
                byte g = Channel(components[0]);
                return (g, g, g);
            }
            case "DeviceRGB" or "CalRGB" when components.Count >= 3:
                return (Channel(components[0]), Channel(components[1]), Channel(components[2]));
            case "DeviceCMYK" when components.Count >= 4:
                return Cmyk(components[0], components[1], components[2], components[3]);
            case "Lab" when components.Count >= 3:
                return Lab(components[0], components[1], components[2]);
        }

        // Unknown space: infer from component count (matches ColorConverter's fallback).
        return components.Count switch
        {
            >= 4 => Cmyk(components[0], components[1], components[2], components[3]),
            >= 3 => (Channel(components[0]), Channel(components[1]), Channel(components[2])),
            _ => Mono(components[0])
        };
    }

    public static byte AlphaByte(double alpha) => (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255);

    private static byte Channel(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
    private static (byte, byte, byte) Mono(double v) { byte g = Channel(v); return (g, g, g); }

    private static (byte, byte, byte) Cmyk(double c, double m, double y, double k) =>
        (Channel(1 - Math.Min(1, c * (1 - k) + k)),
         Channel(1 - Math.Min(1, m * (1 - k) + k)),
         Channel(1 - Math.Min(1, y * (1 - k) + k)));

    private static (byte, byte, byte) Lab(double l, double a, double b)
    {
        // PDF Lab components arrive already in CIE-Lab ranges (L 0-100, a/b signed).
        // Match ColorConverter's exact Unicolour API: new Unicolour(ColourSpace.Lab, L, a, b)
        var unicolour = new Unicolour(ColourSpace.Lab, l, a, b);
        Rgb rgb = unicolour.Rgb;

        var rByte = (byte)Math.Round(Math.Clamp(rgb.R, 0.0, 1.0) * 255);
        var gByte = (byte)Math.Round(Math.Clamp(rgb.G, 0.0, 1.0) * 255);
        var bByte = (byte)Math.Round(Math.Clamp(rgb.B, 0.0, 1.0) * 255);

        return (rByte, gByte, bByte);
    }
}
