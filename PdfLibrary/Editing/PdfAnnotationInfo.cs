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

    /// <summary>The line endpoints (<c>/L</c>) for a Line annotation, in PDF user space, or null.</summary>
    public (double X1, double Y1, double X2, double Y2)? LineEndpoints { get; init; }

    /// <summary>The freehand paths (<c>/InkList</c>) for an Ink annotation, in PDF user space, or null.</summary>
    public IReadOnlyList<IReadOnlyList<(double X, double Y)>>? InkPaths { get; init; }

    /// <summary>The text-justification (<c>/Q</c>) for a FreeText annotation (0=left,1=center,2=right), or null.</summary>
    public int? Quadding { get; init; }

    /// <summary>The default-appearance string (<c>/DA</c>) for a FreeText annotation, or null.</summary>
    public string? DefaultAppearance { get; init; }
}
