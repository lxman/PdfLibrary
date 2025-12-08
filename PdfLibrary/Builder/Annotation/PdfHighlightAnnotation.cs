using PdfLibrary.Builder.Page;

namespace PdfLibrary.Builder.Annotation;

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