using PdfLibrary.Builder.Page;

namespace PdfLibrary.Builder.Bookmark;

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
