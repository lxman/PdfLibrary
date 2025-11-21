namespace PdfLibrary.Builder;

/// <summary>
/// Extension methods for fluent unit specification.
/// Allows syntax like: 1.Inches(), 72.Pt(), 25.Mm(), etc.
/// </summary>
public static class PdfUnitExtensions
{
    #region Points

    /// <summary>
    /// Create a length in points (PDF's native unit, 72 points = 1 inch)
    /// </summary>
    public static PdfLength Pt(this int value) => PdfLength.FromPoints(value);

    /// <summary>
    /// Create a length in points (PDF's native unit, 72 points = 1 inch)
    /// </summary>
    public static PdfLength Pt(this double value) => PdfLength.FromPoints(value);

    #endregion

    #region Inches

    /// <summary>
    /// Create a length in inches (1 inch = 72 points)
    /// </summary>
    public static PdfLength Inches(this int value) => PdfLength.FromInches(value);

    /// <summary>
    /// Create a length in inches (1 inch = 72 points)
    /// </summary>
    public static PdfLength Inches(this double value) => PdfLength.FromInches(value);

    /// <summary>
    /// Create a length in inches (1 inch = 72 points). Shorthand for Inches()
    /// </summary>
    public static PdfLength In(this int value) => PdfLength.FromInches(value);

    /// <summary>
    /// Create a length in inches (1 inch = 72 points). Shorthand for Inches()
    /// </summary>
    public static PdfLength In(this double value) => PdfLength.FromInches(value);

    #endregion

    #region Millimeters

    /// <summary>
    /// Create a length in millimeters (1 mm ≈ 2.835 points)
    /// </summary>
    public static PdfLength Mm(this int value) => PdfLength.FromMillimeters(value);

    /// <summary>
    /// Create a length in millimeters (1 mm ≈ 2.835 points)
    /// </summary>
    public static PdfLength Mm(this double value) => PdfLength.FromMillimeters(value);

    #endregion

    #region Centimeters

    /// <summary>
    /// Create a length in centimeters (1 cm ≈ 28.35 points)
    /// </summary>
    public static PdfLength Cm(this int value) => PdfLength.FromCentimeters(value);

    /// <summary>
    /// Create a length in centimeters (1 cm ≈ 28.35 points)
    /// </summary>
    public static PdfLength Cm(this double value) => PdfLength.FromCentimeters(value);

    #endregion

    #region Pixels

    /// <summary>
    /// Create a length from pixels using standard screen DPI (96)
    /// </summary>
    public static PdfLength Px(this int value) => PdfLength.FromPixels(value, 96);

    /// <summary>
    /// Create a length from pixels using the specified DPI
    /// </summary>
    public static PdfLength Px(this int value, int dpi) => PdfLength.FromPixels(value, dpi);

    #endregion
}
