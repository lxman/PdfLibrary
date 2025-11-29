namespace PdfLibrary.Builder;

/// <summary>
/// Units of measurement for PDF coordinates
/// </summary>
public enum PdfUnit
{
    /// <summary>
    /// PDF points (72 points per inch) - native PDF unit
    /// </summary>
    Points,

    /// <summary>
    /// Inches (1 inch = 72 points)
    /// </summary>
    Inches,

    /// <summary>
    /// Millimeters (1 mm = 2.834645669 points)
    /// </summary>
    Millimeters,

    /// <summary>
    /// Centimeters (1 cm = 28.34645669 points)
    /// </summary>
    Centimeters
}

/// <summary>
/// Coordinate system origin for specifying positions
/// </summary>
public enum PdfOrigin
{
    /// <summary>
    /// Origin in the bottom-left corner (PDF native)
    /// Y increases upward
    /// </summary>
    BottomLeft,

    /// <summary>
    /// Origin in the top-left corner (screen-like)
    /// Y increases downward
    /// </summary>
    TopLeft
}

/// <summary>
/// Standard page sizes
/// </summary>
public static class PdfPageSize
{
    // US sizes
    public static readonly PdfSize Letter = new(612, 792);      // 8.5" x 11"
    public static readonly PdfSize Legal = new(612, 1008);      // 8.5" x 14"
    public static readonly PdfSize Tabloid = new(792, 1224);    // 11" x 17"

    // ISO A sizes
    public static readonly PdfSize A0 = new(2384, 3370);
    public static readonly PdfSize A1 = new(1684, 2384);
    public static readonly PdfSize A2 = new(1191, 1684);
    public static readonly PdfSize A3 = new(842, 1191);
    public static readonly PdfSize A4 = new(595, 842);
    public static readonly PdfSize A5 = new(420, 595);
    public static readonly PdfSize A6 = new(298, 420);

    // ISO B sizes
    public static readonly PdfSize B4 = new(729, 1032);
    public static readonly PdfSize B5 = new(516, 729);
}

/// <summary>
/// Represents a page size in points
/// </summary>
public readonly struct PdfSize(double width, double height)
{
    public double Width { get; } = width;
    public double Height { get; } = height;

    /// <summary>
    /// Create a size from inches
    /// </summary>
    public static PdfSize FromInches(double width, double height)
        => new(width * 72, height * 72);

    /// <summary>
    /// Create a size from millimeters
    /// </summary>
    public static PdfSize FromMillimeters(double width, double height)
        => new(width * 2.834645669, height * 2.834645669);

    /// <summary>
    /// Returns the landscape orientation of this size
    /// </summary>
    public PdfSize Landscape => Width > Height ? this : new PdfSize(Height, Width);

    /// <summary>
    /// Returns the portrait orientation of this size
    /// </summary>
    public PdfSize Portrait => Height > Width ? this : new PdfSize(Height, Width);
}
