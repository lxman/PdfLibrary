using PdfLibrary.Builder.Page;

namespace PdfLibrary.Builder.Annotation;

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
