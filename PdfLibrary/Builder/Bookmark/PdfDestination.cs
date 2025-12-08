namespace PdfLibrary.Builder.Bookmark;

/// <summary>
/// Represents a destination in a PDF document (page + view parameters)
/// </summary>
public class PdfDestination
{
    /// <summary>
    /// The page index (0-based) this destination refers to
    /// </summary>
    public int PageIndex { get; internal set; }

    /// <summary>
    /// The type of destination view
    /// </summary>
    public PdfDestinationType Type { get; internal set; } = PdfDestinationType.XYZ;

    /// <summary>
    /// Left coordinate for XYZ, FitV, FitBV, FitR destinations (null = unchanged)
    /// </summary>
    public double? Left { get; internal set; }

    /// <summary>
    /// Top coordinate for XYZ, FitH, FitBH, FitR destinations (null = unchanged)
    /// </summary>
    public double? Top { get; internal set; }

    /// <summary>
    /// Right coordinate for FitR destination
    /// </summary>
    public double? Right { get; internal set; }

    /// <summary>
    /// Bottom coordinate for FitR destination
    /// </summary>
    public double? Bottom { get; internal set; }

    /// <summary>
    /// Zoom factor for XYZ destination (null = unchanged, 0 = fit)
    /// </summary>
    public double? Zoom { get; internal set; }
}