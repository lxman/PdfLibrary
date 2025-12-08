namespace PdfLibrary.Builder.Annotation;

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