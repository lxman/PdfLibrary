using PdfLibrary.Builder.Page;

namespace PdfLibrary.Builder.Bookmark;

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
        foreach (PdfBookmark child in Children)
        {
            if (child.IsOpen)
            {
                count += child.GetDescendantCount();
            }
        }
        return count;
    }
}