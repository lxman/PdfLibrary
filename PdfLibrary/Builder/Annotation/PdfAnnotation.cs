namespace PdfLibrary.Builder.Annotation;

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