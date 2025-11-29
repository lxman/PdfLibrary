namespace PdfLibrary.Builder;

/// <summary>
/// Represents a bookmark (outline item) in a PDF document.
/// Bookmarks provide a hierarchical table of contents for navigation.
/// </summary>
public class PdfBookmark
{
    private static int _nextId = 1;

    /// <summary>
    /// Unique identifier for this bookmark (used internally)
    /// </summary>
    internal int Id { get; }

    /// <summary>
    /// The display title of the bookmark
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// The destination for this bookmark (page and position)
    /// </summary>
    public PdfDestination Destination { get; internal set; }

    /// <summary>
    /// Child bookmarks (for nested hierarchy)
    /// </summary>
    public List<PdfBookmark> Children { get; } = [];

    /// <summary>
    /// Whether this bookmark is initially expanded (showing children)
    /// </summary>
    public bool IsOpen { get; internal set; } = true;

    /// <summary>
    /// Text color for the bookmark title (null = default black)
    /// </summary>
    public PdfColor? TextColor { get; internal set; }

    /// <summary>
    /// Whether the bookmark title is bold
    /// </summary>
    public bool IsBold { get; internal set; }

    /// <summary>
    /// Whether the bookmark title is italic
    /// </summary>
    public bool IsItalic { get; internal set; }

    /// <summary>
    /// Creates a new bookmark with the specified title
    /// </summary>
    internal PdfBookmark(string title)
    {
        Id = _nextId++;
        Title = title;
        Destination = new PdfDestination(); // Default: first page, top
    }

    /// <summary>
    /// Creates a new bookmark with title and destination
    /// </summary>
    internal PdfBookmark(string title, PdfDestination destination)
    {
        Id = _nextId++;
        Title = title;
        Destination = destination;
    }

    /// <summary>
    /// Gets the total count of visible descendants (for /Count entry)
    /// </summary>
    internal int GetDescendantCount()
    {
        if (Children.Count == 0) return 0;

        int count = Children.Count;
        foreach (var child in Children)
        {
            if (child.IsOpen)
            {
                count += child.GetDescendantCount();
            }
        }
        return count;
    }
}

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

/// <summary>
/// Types of PDF destinations that control how the page is displayed
/// </summary>
public enum PdfDestinationType
{
    /// <summary>
    /// Display page at specified coordinates and zoom level [/XYZ left top zoom]
    /// </summary>
    XYZ,

    /// <summary>
    /// Fit entire page in window [/Fit]
    /// </summary>
    Fit,

    /// <summary>
    /// Fit page width in window at specified top coordinate [/FitH top]
    /// </summary>
    FitH,

    /// <summary>
    /// Fit page height in window at specified left coordinate [/FitV left]
    /// </summary>
    FitV,

    /// <summary>
    /// Fit specified rectangle in window [/FitR left bottom right top]
    /// </summary>
    FitR,

    /// <summary>
    /// Fit bounding box of page contents in window [/FitB]
    /// </summary>
    FitB,

    /// <summary>
    /// Fit width of bounding box in window [/FitBH top]
    /// </summary>
    FitBH,

    /// <summary>
    /// Fit height of bounding box in window [/FitBV left]
    /// </summary>
    FitBV
}

/// <summary>
/// Fluent builder for configuring bookmark properties
/// </summary>
public class PdfBookmarkBuilder
{
    private readonly PdfBookmark _bookmark;

    internal PdfBookmarkBuilder(PdfBookmark bookmark)
    {
        _bookmark = bookmark;
    }

    /// <summary>
    /// Gets the underlying bookmark
    /// </summary>
    public PdfBookmark Bookmark => _bookmark;

    /// <summary>
    /// Set the destination page (0-based index)
    /// </summary>
    public PdfBookmarkBuilder ToPage(int pageIndex)
    {
        _bookmark.Destination.PageIndex = pageIndex;
        return this;
    }

    /// <summary>
    /// Set destination to show page at specific coordinates with optional zoom
    /// </summary>
    public PdfBookmarkBuilder AtPosition(double? left = null, double? top = null, double? zoom = null)
    {
        _bookmark.Destination.Type = PdfDestinationType.XYZ;
        _bookmark.Destination.Left = left;
        _bookmark.Destination.Top = top;
        _bookmark.Destination.Zoom = zoom;
        return this;
    }

    /// <summary>
    /// Set destination to fit entire page in window
    /// </summary>
    public PdfBookmarkBuilder FitPage()
    {
        _bookmark.Destination.Type = PdfDestinationType.Fit;
        return this;
    }

    /// <summary>
    /// Set destination to fit page width at specified top position
    /// </summary>
    public PdfBookmarkBuilder FitWidth(double? top = null)
    {
        _bookmark.Destination.Type = PdfDestinationType.FitH;
        _bookmark.Destination.Top = top;
        return this;
    }

    /// <summary>
    /// Set destination to fit page height at specified left position
    /// </summary>
    public PdfBookmarkBuilder FitHeight(double? left = null)
    {
        _bookmark.Destination.Type = PdfDestinationType.FitV;
        _bookmark.Destination.Left = left;
        return this;
    }

    /// <summary>
    /// Set destination to fit a specific rectangle
    /// </summary>
    public PdfBookmarkBuilder FitRectangle(double left, double bottom, double right, double top)
    {
        _bookmark.Destination.Type = PdfDestinationType.FitR;
        _bookmark.Destination.Left = left;
        _bookmark.Destination.Bottom = bottom;
        _bookmark.Destination.Right = right;
        _bookmark.Destination.Top = top;
        return this;
    }

    /// <summary>
    /// Make the bookmark initially collapsed (children hidden)
    /// </summary>
    public PdfBookmarkBuilder Collapsed()
    {
        _bookmark.IsOpen = false;
        return this;
    }

    /// <summary>
    /// Make the bookmark initially expanded (children visible) - this is the default
    /// </summary>
    public PdfBookmarkBuilder Expanded()
    {
        _bookmark.IsOpen = true;
        return this;
    }

    /// <summary>
    /// Set the text color of the bookmark title
    /// </summary>
    public PdfBookmarkBuilder WithColor(PdfColor color)
    {
        _bookmark.TextColor = color;
        return this;
    }

    /// <summary>
    /// Make the bookmark title bold
    /// </summary>
    public PdfBookmarkBuilder Bold()
    {
        _bookmark.IsBold = true;
        return this;
    }

    /// <summary>
    /// Make the bookmark title italic
    /// </summary>
    public PdfBookmarkBuilder Italic()
    {
        _bookmark.IsItalic = true;
        return this;
    }

    /// <summary>
    /// Add a child bookmark
    /// </summary>
    public PdfBookmarkBuilder AddChild(string title, Action<PdfBookmarkBuilder>? configure = null)
    {
        var child = new PdfBookmark(title);
        if (configure != null)
        {
            var childBuilder = new PdfBookmarkBuilder(child);
            configure(childBuilder);
        }
        _bookmark.Children.Add(child);
        return this;
    }

    /// <summary>
    /// Add a child bookmark and get a reference to it
    /// </summary>
    public PdfBookmarkBuilder AddChild(string title, out PdfBookmark child, Action<PdfBookmarkBuilder>? configure = null)
    {
        child = new PdfBookmark(title);
        if (configure != null)
        {
            var childBuilder = new PdfBookmarkBuilder(child);
            configure(childBuilder);
        }
        _bookmark.Children.Add(child);
        return this;
    }

    /// <summary>
    /// Implicit conversion to PdfBookmark for convenience
    /// </summary>
    public static implicit operator PdfBookmark(PdfBookmarkBuilder builder) => builder._bookmark;
}
