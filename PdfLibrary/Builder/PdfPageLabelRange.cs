namespace PdfLibrary.Builder;

/// <summary>
/// Represents a page label range - defines how pages are labeled starting from a specific page index
/// </summary>
public class PdfPageLabelRange
{
    /// <summary>
    /// The 0-based page index where this labeling scheme starts
    /// </summary>
    public int StartPageIndex { get; }

    /// <summary>
    /// The numbering style to use
    /// </summary>
    public PdfPageLabelStyle Style { get; internal set; } = PdfPageLabelStyle.Decimal;

    /// <summary>
    /// Optional prefix to appear before the page number (e.g., "A-" for "A-1", "A-2")
    /// </summary>
    public string? Prefix { get; internal set; }

    /// <summary>
    /// The numeric value of the first page in this range (default = 1)
    /// </summary>
    public int StartNumber { get; internal set; } = 1;

    internal PdfPageLabelRange(int startPageIndex)
    {
        StartPageIndex = startPageIndex;
    }
}