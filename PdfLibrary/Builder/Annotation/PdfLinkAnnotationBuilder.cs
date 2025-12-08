namespace PdfLibrary.Builder.Annotation;

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