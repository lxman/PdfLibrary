namespace PdfLibrary.Builder.Bookmark;

/// <summary>
/// Represents a destination in a PDF document (page + view parameters)
/// </summary>
public class PdfDestination
{
    // ── Public static factories (additive; existing internal setters and builder unchanged) ──

    /// <summary>Navigate to <paramref name="pageIndex"/> with current scroll position and zoom (/XYZ null null null).</summary>
    public static PdfDestination ToPage(int pageIndex) =>
        new() { PageIndex = pageIndex, Type = PdfDestinationType.XYZ, Left = null, Top = null, Zoom = null };

    /// <summary>Fit the entire page in the viewer window (/Fit).</summary>
    public static PdfDestination FitPage(int pageIndex) =>
        new() { PageIndex = pageIndex, Type = PdfDestinationType.Fit };

    /// <summary>Fit the page width, scrolling to <paramref name="top"/> (/FitH top).</summary>
    public static PdfDestination FitWidth(int pageIndex, double? top) =>
        new() { PageIndex = pageIndex, Type = PdfDestinationType.FitH, Top = top };

    /// <summary>Fit the page height, scrolling to <paramref name="left"/> (/FitV left).</summary>
    public static PdfDestination FitHeight(int pageIndex, double? left) =>
        new() { PageIndex = pageIndex, Type = PdfDestinationType.FitV, Left = left };

    /// <summary>Navigate to an explicit position and zoom level (/XYZ left top zoom).</summary>
    public static PdfDestination At(int pageIndex, double? left, double? top, double? zoom) =>
        new() { PageIndex = pageIndex, Type = PdfDestinationType.XYZ, Left = left, Top = top, Zoom = zoom };

    /// <summary>Fit the specified rectangle into the viewer window (/FitR left bottom right top).</summary>
    public static PdfDestination FitRect(int pageIndex, double left, double bottom, double right, double top) =>
        new() { PageIndex = pageIndex, Type = PdfDestinationType.FitR, Left = left, Bottom = bottom, Right = right, Top = top };


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