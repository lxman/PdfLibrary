using PdfLibrary.Builder;

namespace PdfLibrary.Editing;

/// <summary>
/// A read-only view of a single annotation on a page, returned by
/// <see cref="PdfPageCollection.GetAnnotations(int)"/>.
/// </summary>
public sealed class PdfAnnotationInfo
{
    /// <summary>The annotation subtype (<c>/Subtype</c>), e.g. "Text", "Link", "Highlight". Empty if absent.</summary>
    public required string Subtype { get; init; }

    /// <summary>The annotation rectangle (<c>/Rect</c>) in PDF user space; a zero rect if absent or malformed.</summary>
    public required PdfRect Rect { get; init; }

    /// <summary>The annotation's text contents (<c>/Contents</c>), or null if absent.</summary>
    public string? Contents { get; init; }
}
