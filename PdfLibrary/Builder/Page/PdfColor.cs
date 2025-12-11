namespace PdfLibrary.Builder.Page;

/// <summary>
/// Represents a color in PDF with support for multiple color spaces
/// </summary>
public readonly struct PdfColor
{
    /// <summary>The color space this color is defined in</summary>
    public PdfColorSpace ColorSpace { get; }

    /// <summary>Color components (interpretation depends on ColorSpace)</summary>
    public double[] Components { get; }

    /// <summary>Colorant name for Separation color spaces (null for other color spaces)</summary>
    public string? ColorantName { get; }

    // Convenience accessors for RGB
    public double R => ColorSpace == PdfColorSpace.DeviceRGB ? Components[0] : 0;
    public double G => ColorSpace == PdfColorSpace.DeviceRGB ? Components[1] : 0;
    public double B => ColorSpace == PdfColorSpace.DeviceRGB ? Components[2] : 0;

    // Convenience accessors for Gray
    public double GrayValue => ColorSpace == PdfColorSpace.DeviceGray ? Components[0] : 0;

    // Convenience accessors for CMYK
    public double C => ColorSpace == PdfColorSpace.DeviceCMYK ? Components[0] : 0;
    public double M => ColorSpace == PdfColorSpace.DeviceCMYK ? Components[1] : 0;
    public double Y => ColorSpace == PdfColorSpace.DeviceCMYK ? Components[2] : 0;
    public double K => ColorSpace == PdfColorSpace.DeviceCMYK ? Components[3] : 0;

    // Convenience accessor for Separation
    public double Tint => ColorSpace == PdfColorSpace.Separation ? Components[0] : 0;

    /// <summary>
    /// Create an RGB color (values 0-1)
    /// </summary>
    public PdfColor(double r, double g, double b)
    {
        ColorSpace = PdfColorSpace.DeviceRGB;
        Components = [r, g, b];
        ColorantName = null;
    }

    /// <summary>
    /// Create a color with explicit color space and components (internal constructor)
    /// </summary>
    private PdfColor(PdfColorSpace colorSpace, double[] components, string? colorantName = null)
    {
        ColorSpace = colorSpace;
        Components = components;
        ColorantName = colorantName;
    }

    /// <summary>
    /// Create a color with explicit color space and components
    /// </summary>
    public static PdfColor FromComponents(PdfColorSpace colorSpace, params double[] components)
        => new(colorSpace, components);

    /// <summary>
    /// Create a color from 0-255 RGB values
    /// </summary>
    public static PdfColor FromRgb(int r, int g, int b)
        => new(r / 255.0, g / 255.0, b / 255.0);

    /// <summary>
    /// Create a grayscale color using DeviceGray color space (0 = black, 1 = white)
    /// </summary>
    public static PdfColor FromGray(double value)
        => FromComponents(PdfColorSpace.DeviceGray, value);

    /// <summary>
    /// Create a grayscale color as RGB (0 = black, 1 = white) - legacy method
    /// </summary>
    public static PdfColor Gray(double value) => new(value, value, value);

    /// <summary>
    /// Create a CMYK color (values 0-1)
    /// </summary>
    public static PdfColor FromCmyk(double c, double m, double y, double k)
        => FromComponents(PdfColorSpace.DeviceCMYK, c, m, y, k);

    /// <summary>
    /// Create a CMYK color from 0-100 percentage values
    /// </summary>
    public static PdfColor FromCmykPercent(double c, double m, double y, double k)
        => FromComponents(PdfColorSpace.DeviceCMYK, c / 100.0, m / 100.0, y / 100.0, k / 100.0);

    /// <summary>
    /// Create a Separation color (spot color) with a colorant name and tint value (0-1)
    /// </summary>
    public static PdfColor FromSeparation(string colorantName, double tint)
        => new(PdfColorSpace.Separation, [Math.Clamp(tint, 0, 1)], colorantName);

    // Common colors (DeviceRGB) - use explicit constructor to avoid ambiguity with params overload
    public static readonly PdfColor Black = new PdfColor(0, 0, 0);
    public static readonly PdfColor White = new PdfColor(1, 1, 1);
    public static readonly PdfColor Red = new PdfColor(1, 0, 0);
    public static readonly PdfColor Green = new PdfColor(0, 1, 0);
    public static readonly PdfColor Blue = new PdfColor(0, 0, 1);
    public static readonly PdfColor Yellow = new PdfColor(1, 1, 0);
    public static readonly PdfColor Cyan = new PdfColor(0, 1, 1);
    public static readonly PdfColor Magenta = new PdfColor(1, 0, 1);
    public static readonly PdfColor LightGray = new PdfColor(0.75, 0.75, 0.75);
    public static readonly PdfColor DarkGray = new PdfColor(0.25, 0.25, 0.25);

    // Common CMYK colors
    public static readonly PdfColor CmykBlack = FromCmyk(0, 0, 0, 1);
    public static readonly PdfColor CmykWhite = FromCmyk(0, 0, 0, 0);
    public static readonly PdfColor CmykCyan = FromCmyk(1, 0, 0, 0);
    public static readonly PdfColor CmykMagenta = FromCmyk(0, 1, 0, 0);
    public static readonly PdfColor CmykYellow = FromCmyk(0, 0, 1, 0);
    public static readonly PdfColor CmykRed = FromCmyk(0, 1, 1, 0);
    public static readonly PdfColor CmykGreen = FromCmyk(1, 0, 1, 0);
    public static readonly PdfColor CmykBlue = FromCmyk(1, 1, 0, 0);
}