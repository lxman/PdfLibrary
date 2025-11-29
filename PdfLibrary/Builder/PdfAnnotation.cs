namespace PdfLibrary.Builder;

/// <summary>
/// Base class for PDF annotations.
/// Annotations are interactive elements added to pages (links, notes, highlights, etc.)
/// </summary>
public abstract class PdfAnnotation
{
    private static int _nextId = 1;

    /// <summary>
    /// Unique identifier for this annotation (used internally)
    /// </summary>
    internal int Id { get; }

    /// <summary>
    /// The annotation subtype (e.g., "Link", "Text", "Highlight")
    /// </summary>
    public abstract string Subtype { get; }

    /// <summary>
    /// The rectangle defining the annotation's location on the page (in PDF coordinates)
    /// </summary>
    public PdfRect Rect { get; internal set; }

    /// <summary>
    /// Optional border style for the annotation
    /// </summary>
    public PdfAnnotationBorder? Border { get; internal set; }

    /// <summary>
    /// Optional annotation flags
    /// </summary>
    public PdfAnnotationFlags Flags { get; internal set; } = PdfAnnotationFlags.None;

    protected PdfAnnotation(PdfRect rect)
    {
        Id = _nextId++;
        Rect = rect;
    }
}

/// <summary>
/// Border style for annotations
/// </summary>
public class PdfAnnotationBorder
{
    /// <summary>
    /// Horizontal corner radius
    /// </summary>
    public double HorizontalRadius { get; set; }

    /// <summary>
    /// Vertical corner radius
    /// </summary>
    public double VerticalRadius { get; set; }

    /// <summary>
    /// Border width (0 = invisible)
    /// </summary>
    public double Width { get; set; } = 1;

    /// <summary>
    /// Dash pattern (null = solid)
    /// </summary>
    public double[]? DashPattern { get; set; }

    /// <summary>
    /// No visible border
    /// </summary>
    public static PdfAnnotationBorder None => new() { Width = 0 };

    /// <summary>
    /// Thin border (0.5 pt)
    /// </summary>
    public static PdfAnnotationBorder Thin => new() { Width = 0.5 };

    /// <summary>
    /// Standard border (1 pt)
    /// </summary>
    public static PdfAnnotationBorder Standard => new() { Width = 1 };
}

/// <summary>
/// Annotation flags (F entry in annotation dictionary)
/// </summary>
[Flags]
public enum PdfAnnotationFlags
{
    None = 0,
    Invisible = 1 << 0,
    Hidden = 1 << 1,
    Print = 1 << 2,
    NoZoom = 1 << 3,
    NoRotate = 1 << 4,
    NoView = 1 << 5,
    ReadOnly = 1 << 6,
    Locked = 1 << 7,
    ToggleNoView = 1 << 8,
    LockedContents = 1 << 9
}

/// <summary>
/// Link annotation - clickable area that navigates to a destination or URL
/// </summary>
public class PdfLinkAnnotation : PdfAnnotation
{
    public override string Subtype => "Link";

    /// <summary>
    /// The action to perform when clicked (either a destination or URI)
    /// </summary>
    public PdfLinkAction Action { get; internal set; }

    /// <summary>
    /// Highlight mode when the link is clicked
    /// </summary>
    public PdfLinkHighlightMode HighlightMode { get; internal set; } = PdfLinkHighlightMode.Invert;

    internal PdfLinkAnnotation(PdfRect rect, PdfLinkAction action) : base(rect)
    {
        Action = action;
        Border = PdfAnnotationBorder.None; // Links typically have no border
    }
}

/// <summary>
/// Base class for link actions
/// </summary>
public abstract class PdfLinkAction
{
    public abstract string ActionType { get; }
}

/// <summary>
/// Action that navigates to a page destination within the document
/// </summary>
public class PdfGoToAction : PdfLinkAction
{
    public override string ActionType => "GoTo";

    /// <summary>
    /// The destination to navigate to
    /// </summary>
    public PdfDestination Destination { get; }

    public PdfGoToAction(PdfDestination destination)
    {
        Destination = destination;
    }

    public PdfGoToAction(int pageIndex)
    {
        Destination = new PdfDestination { PageIndex = pageIndex, Type = PdfDestinationType.Fit };
    }
}

/// <summary>
/// Action that opens an external URI (URL)
/// </summary>
public class PdfUriAction : PdfLinkAction
{
    public override string ActionType => "URI";

    /// <summary>
    /// The URI to open
    /// </summary>
    public string Uri { get; }

    public PdfUriAction(string uri)
    {
        Uri = uri ?? throw new ArgumentNullException(nameof(uri));
    }
}

/// <summary>
/// Highlight mode for link annotations
/// </summary>
public enum PdfLinkHighlightMode
{
    /// <summary>
    /// No highlighting
    /// </summary>
    None,

    /// <summary>
    /// Invert the colors within the annotation rectangle
    /// </summary>
    Invert,

    /// <summary>
    /// Invert the border of the annotation
    /// </summary>
    Outline,

    /// <summary>
    /// Display the annotation as if it were being pushed
    /// </summary>
    Push
}

/// <summary>
/// Text annotation (sticky note / comment)
/// </summary>
public class PdfTextAnnotation : PdfAnnotation
{
    public override string Subtype => "Text";

    /// <summary>
    /// The text content of the note
    /// </summary>
    public string Contents { get; internal set; } = string.Empty;

    /// <summary>
    /// The icon name for the note
    /// </summary>
    public PdfTextAnnotationIcon Icon { get; internal set; } = PdfTextAnnotationIcon.Note;

    /// <summary>
    /// Whether the note popup is initially open
    /// </summary>
    public bool IsOpen { get; internal set; }

    /// <summary>
    /// Color of the annotation icon
    /// </summary>
    public PdfColor? Color { get; internal set; }

    internal PdfTextAnnotation(PdfRect rect, string contents) : base(rect)
    {
        Contents = contents;
    }
}

/// <summary>
/// Standard icons for text annotations
/// </summary>
public enum PdfTextAnnotationIcon
{
    Comment,
    Key,
    Note,
    Help,
    NewParagraph,
    Paragraph,
    Insert
}

/// <summary>
/// Highlight annotation - highlights text on the page
/// </summary>
public class PdfHighlightAnnotation : PdfAnnotation
{
    public override string Subtype => "Highlight";

    /// <summary>
    /// The highlight color
    /// </summary>
    public PdfColor Color { get; internal set; } = PdfColor.Yellow;

    /// <summary>
    /// QuadPoints defining the highlighted text regions
    /// </summary>
    public List<PdfQuadPoints> QuadPoints { get; } = [];

    internal PdfHighlightAnnotation(PdfRect rect) : base(rect)
    {
    }
}

/// <summary>
/// Four points defining a quadrilateral region (used for text markup annotations)
/// Points are specified in order: bottom-left, bottom-right, top-left, top-right
/// </summary>
public readonly struct PdfQuadPoints
{
    public double X1 { get; }
    public double Y1 { get; }
    public double X2 { get; }
    public double Y2 { get; }
    public double X3 { get; }
    public double Y3 { get; }
    public double X4 { get; }
    public double Y4 { get; }

    public PdfQuadPoints(double x1, double y1, double x2, double y2,
                         double x3, double y3, double x4, double y4)
    {
        X1 = x1; Y1 = y1;
        X2 = x2; Y2 = y2;
        X3 = x3; Y3 = y3;
        X4 = x4; Y4 = y4;
    }

    /// <summary>
    /// Create quad points from a rectangle
    /// </summary>
    public static PdfQuadPoints FromRect(PdfRect rect)
    {
        return new PdfQuadPoints(
            rect.Left, rect.Bottom,   // bottom-left
            rect.Right, rect.Bottom,  // bottom-right
            rect.Left, rect.Top,      // top-left
            rect.Right, rect.Top      // top-right
        );
    }
}

/// <summary>
/// Fluent builder for configuring link annotations
/// </summary>
public class PdfLinkAnnotationBuilder
{
    private readonly PdfLinkAnnotation _annotation;

    internal PdfLinkAnnotationBuilder(PdfLinkAnnotation annotation)
    {
        _annotation = annotation;
    }

    /// <summary>
    /// Set the highlight mode
    /// </summary>
    public PdfLinkAnnotationBuilder WithHighlight(PdfLinkHighlightMode mode)
    {
        _annotation.HighlightMode = mode;
        return this;
    }

    /// <summary>
    /// Add a visible border
    /// </summary>
    public PdfLinkAnnotationBuilder WithBorder(double width = 1)
    {
        _annotation.Border = new PdfAnnotationBorder { Width = width };
        return this;
    }

    /// <summary>
    /// Remove the border (default for links)
    /// </summary>
    public PdfLinkAnnotationBuilder NoBorder()
    {
        _annotation.Border = PdfAnnotationBorder.None;
        return this;
    }

    /// <summary>
    /// Make the annotation printable
    /// </summary>
    public PdfLinkAnnotationBuilder Printable()
    {
        _annotation.Flags |= PdfAnnotationFlags.Print;
        return this;
    }

    /// <summary>
    /// Gets the underlying annotation
    /// </summary>
    public PdfLinkAnnotation Annotation => _annotation;
}

/// <summary>
/// Fluent builder for configuring text annotations
/// </summary>
public class PdfTextAnnotationBuilder
{
    private readonly PdfTextAnnotation _annotation;

    internal PdfTextAnnotationBuilder(PdfTextAnnotation annotation)
    {
        _annotation = annotation;
    }

    /// <summary>
    /// Set the icon type
    /// </summary>
    public PdfTextAnnotationBuilder WithIcon(PdfTextAnnotationIcon icon)
    {
        _annotation.Icon = icon;
        return this;
    }

    /// <summary>
    /// Set the annotation color
    /// </summary>
    public PdfTextAnnotationBuilder WithColor(PdfColor color)
    {
        _annotation.Color = color;
        return this;
    }

    /// <summary>
    /// Make the popup initially open
    /// </summary>
    public PdfTextAnnotationBuilder Open()
    {
        _annotation.IsOpen = true;
        return this;
    }

    /// <summary>
    /// Make the annotation printable
    /// </summary>
    public PdfTextAnnotationBuilder Printable()
    {
        _annotation.Flags |= PdfAnnotationFlags.Print;
        return this;
    }

    /// <summary>
    /// Gets the underlying annotation
    /// </summary>
    public PdfTextAnnotation Annotation => _annotation;
}

/// <summary>
/// Fluent builder for configuring highlight annotations
/// </summary>
public class PdfHighlightAnnotationBuilder
{
    private readonly PdfHighlightAnnotation _annotation;

    internal PdfHighlightAnnotationBuilder(PdfHighlightAnnotation annotation)
    {
        _annotation = annotation;
    }

    /// <summary>
    /// Set the highlight color
    /// </summary>
    public PdfHighlightAnnotationBuilder WithColor(PdfColor color)
    {
        _annotation.Color = color;
        return this;
    }

    /// <summary>
    /// Add a quad region to highlight
    /// </summary>
    public PdfHighlightAnnotationBuilder AddRegion(PdfQuadPoints quad)
    {
        _annotation.QuadPoints.Add(quad);
        return this;
    }

    /// <summary>
    /// Add a rectangular region to highlight
    /// </summary>
    public PdfHighlightAnnotationBuilder AddRegion(double left, double bottom, double right, double top)
    {
        _annotation.QuadPoints.Add(PdfQuadPoints.FromRect(new PdfRect(left, bottom, right, top)));
        return this;
    }

    /// <summary>
    /// Make the annotation printable
    /// </summary>
    public PdfHighlightAnnotationBuilder Printable()
    {
        _annotation.Flags |= PdfAnnotationFlags.Print;
        return this;
    }

    /// <summary>
    /// Gets the underlying annotation
    /// </summary>
    public PdfHighlightAnnotation Annotation => _annotation;
}
