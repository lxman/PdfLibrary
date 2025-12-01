namespace PdfLibrary.Builder;

/// <summary>
/// Represents a rectangle in PDF coordinates (points, origin bottom-left)
/// </summary>
public readonly struct PdfRect
{
    /// <summary>
    /// Left edge X coordinate in points
    /// </summary>
    public double Left { get; }

    /// <summary>
    /// Bottom edge Y coordinate in points
    /// </summary>
    public double Bottom { get; }

    /// <summary>
    /// Right edge X coordinate in points
    /// </summary>
    public double Right { get; }

    /// <summary>
    /// Top edge Y coordinate in points
    /// </summary>
    public double Top { get; }

    /// <summary>
    /// Width in points
    /// </summary>
    public double Width => Right - Left;

    /// <summary>
    /// Height in points
    /// </summary>
    public double Height => Top - Bottom;

    /// <summary>
    /// Create a rectangle from PDF coordinates (points, origin bottom-left)
    /// </summary>
    /// <param name="left">Left edge X</param>
    /// <param name="bottom">Bottom edge Y</param>
    /// <param name="right">Right edge X</param>
    /// <param name="top">Top edge Y</param>
    public PdfRect(double left, double bottom, double right, double top)
    {
        Left = left;
        Bottom = bottom;
        Right = right;
        Top = top;
    }

    /// <summary>
    /// Create a rectangle from position and size in points
    /// </summary>
    public static PdfRect FromPoints(double x, double y, double width, double height)
    {
        return new PdfRect(x, y, x + width, y + height);
    }

    /// <summary>
    /// Create a rectangle from inches, measuring from the top-left corner of the page
    /// </summary>
    /// <param name="left">Distance from left edge in inches</param>
    /// <param name="top">Distance from top edge in inches</param>
    /// <param name="width">Width in inches</param>
    /// <param name="height">Height in inches</param>
    /// <param name="pageHeight">Page height in points (default: Letter = 792)</param>
    public static PdfRect FromInches(double left, double top, double width, double height, double pageHeight = 792)
    {
        var leftPt = left * 72;
        var widthPt = width * 72;
        var heightPt = height * 72;

        // Convert from top-left origin to bottom-left origin
        var topPt = pageHeight - (top * 72);
        var bottomPt = topPt - heightPt;

        return new PdfRect(leftPt, bottomPt, leftPt + widthPt, topPt);
    }

    /// <summary>
    /// Create a rectangle from millimeters, measuring from the top-left corner of the page
    /// </summary>
    /// <param name="left">Distance from left edge in mm</param>
    /// <param name="top">Distance from top edge in mm</param>
    /// <param name="width">Width in mm</param>
    /// <param name="height">Height in mm</param>
    /// <param name="pageHeight">Page height in points (default: A4 = 842)</param>
    public static PdfRect FromMillimeters(double left, double top, double width, double height, double pageHeight = 842)
    {
        const double mmToPoints = 2.834645669;

        var leftPt = left * mmToPoints;
        var widthPt = width * mmToPoints;
        var heightPt = height * mmToPoints;

        // Convert from top-left origin to bottom-left origin
        var topPt = pageHeight - (top * mmToPoints);
        var bottomPt = topPt - heightPt;

        return new PdfRect(leftPt, bottomPt, leftPt + widthPt, topPt);
    }

    /// <summary>
    /// Create a rectangle with the explicit unit and origin specification
    /// </summary>
    public static PdfRect Create(double x, double y, double width, double height,
        PdfUnit unit, PdfOrigin origin, double pageHeight = 792)
    {
        // Convert to points
        var scale = unit switch
        {
            PdfUnit.Points => 1.0,
            PdfUnit.Inches => 72.0,
            PdfUnit.Millimeters => 2.834645669,
            PdfUnit.Centimeters => 28.34645669,
            _ => 1.0
        };

        var xPt = x * scale;
        var yPt = y * scale;
        var widthPt = width * scale;
        var heightPt = height * scale;

        // Convert origin if needed
        if (origin != PdfOrigin.TopLeft) return new PdfRect(xPt, yPt, xPt + widthPt, yPt + heightPt);
        // y is distance from top, convert to bottom-left coordinates
        var topPt = pageHeight - yPt;
        var bottomPt = topPt - heightPt;
        return new PdfRect(xPt, bottomPt, xPt + widthPt, topPt);

        // Already in bottom-left coordinates
    }

    /// <summary>
    /// Returns the rectangle as a PDF array string: [left bottom right top]
    /// </summary>
    public override string ToString()
    {
        return $"[{Left:F2} {Bottom:F2} {Right:F2} {Top:F2}]";
    }

    /// <summary>
    /// Returns the rectangle as an array of doubles
    /// </summary>
    public double[] ToArray() => [Left, Bottom, Right, Top];
}
