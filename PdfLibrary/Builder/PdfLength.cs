namespace PdfLibrary.Builder;

/// <summary>
/// Represents a length value with a specific unit and optional origin.
/// Can be implicitly converted to double (in points) for use with existing APIs.
/// </summary>
public readonly struct PdfLength
{
    private readonly double _value;
    private readonly PdfUnit _unit;

    /// <summary>
    /// Optional origin specification. If null, uses the page's default origin.
    /// </summary>
    internal readonly PdfOrigin? Origin;

    private PdfLength(double value, PdfUnit unit, PdfOrigin? origin = null)
    {
        _value = value;
        _unit = unit;
        Origin = origin;
    }

    /// <summary>
    /// Create a length in points (PDF's native unit, 72 points = 1 inch)
    /// </summary>
    public static PdfLength FromPoints(double value) => new(value, PdfUnit.Points);

    /// <summary>
    /// Create a length in inches (1 inch = 72 points)
    /// </summary>
    public static PdfLength FromInches(double value) => new(value, PdfUnit.Inches);

    /// <summary>
    /// Create a length in millimeters (1 mm ≈ 2.835 points)
    /// </summary>
    public static PdfLength FromMillimeters(double value) => new(value, PdfUnit.Millimeters);

    /// <summary>
    /// Create a length in centimeters (1 cm ≈ 28.35 points)
    /// </summary>
    public static PdfLength FromCentimeters(double value) => new(value, PdfUnit.Centimeters);

    /// <summary>
    /// Create a length from pixels using the specified DPI
    /// </summary>
    /// <param name="value">Pixel value</param>
    /// <param name="dpi">Dots per inch (default: 96 for standard screens)</param>
    public static PdfLength FromPixels(double value, int dpi = 96)
    {
        // Convert pixels to points: points = pixels * (72 / dpi)
        double points = value * 72.0 / dpi;
        return new(points, PdfUnit.Points);
    }

    /// <summary>
    /// Specify that this length is measured from the top of the page
    /// </summary>
    public PdfLength FromTop() => new(_value, _unit, PdfOrigin.TopLeft);

    /// <summary>
    /// Specify that this length is measured from the bottom of the page
    /// </summary>
    public PdfLength FromBottom() => new(_value, _unit, PdfOrigin.BottomLeft);

    /// <summary>
    /// Convert this length to points in PDF coordinates
    /// </summary>
    /// <param name="pageHeight">Page height in points (needed for top-origin conversions)</param>
    /// <param name="defaultOrigin">Default origin to use if not explicitly specified</param>
    /// <returns>The value in points</returns>
    public double ToPoints(double pageHeight, PdfOrigin defaultOrigin)
    {
        // First convert to points based on unit
        double points = _unit switch
        {
            PdfUnit.Points => _value,
            PdfUnit.Inches => _value * 72,
            PdfUnit.Millimeters => _value * 2.834645669,
            PdfUnit.Centimeters => _value * 28.34645669,
            _ => _value
        };

        // Then adjust for origin
        PdfOrigin origin = Origin ?? defaultOrigin;
        return origin == PdfOrigin.TopLeft
            ? pageHeight - points
            : points;
    }

    /// <summary>
    /// Implicit conversion to double for backward compatibility with existing code
    /// Returns the value in its original unit (NOT converted to points)
    /// </summary>
    public static implicit operator double(PdfLength length) => length._value;

    public override string ToString() => $"{_value} {_unit}";
}
