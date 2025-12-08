using PdfLibrary.Builder.Page;

namespace PdfLibrary.Builder.Annotation;

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