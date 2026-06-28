using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;

namespace PdfLibrary.Editing;

/// <summary>
/// A read-only view of a single annotation on a page, returned by
/// <see cref="PdfPageCollection.GetAnnotations(int)"/>.
/// </summary>
public sealed class PdfAnnotationInfo
{
    /// <summary>The annotation subtype (<c>/Subtype</c>), e.g. "Text", "Link", "Highlight", "Square". Empty if absent.</summary>
    public required string Subtype { get; init; }

    /// <summary>The annotation rectangle (<c>/Rect</c>) in PDF user space; a zero rect if absent or malformed.</summary>
    public required PdfRect Rect { get; init; }

    /// <summary>The annotation's text contents (<c>/Contents</c>), or null if absent.</summary>
    public string? Contents { get; init; }

    /// <summary>
    /// Stable identity for this annotation: the PDF object number of its indirect annotation object.
    /// Pass to <see cref="PdfPageCollection.RemoveAnnotation(int, int)"/> to delete it. 0 if the
    /// annotation is stored as a direct (non-indirect) dictionary.
    /// </summary>
    public int AnnotationId { get; init; }

    /// <summary>The annotation's stroke/border colour (<c>/C</c>), or null if absent.</summary>
    public PdfColor? StrokeColor { get; init; }

    /// <summary>The annotation's interior fill colour (<c>/IC</c>, shape annotations), or null if absent.</summary>
    public PdfColor? InteriorColor { get; init; }

    /// <summary>The annotation's border width (<c>/BS /W</c>), or null if absent.</summary>
    public double? BorderWidth { get; init; }
}
