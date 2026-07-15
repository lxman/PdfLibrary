using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Document;

/// <summary>
/// A read-only view of a document's interactive form dictionary (ISO 32000-1, 12.7): the
/// <c>/AcroForm</c> entry of the document catalog. Built with <see cref="PdfDocument.GetAcroForm"/>.
/// </summary>
/// <remarks>
/// <see cref="TopLevelFieldCount"/> is the length of the <c>/AcroForm</c>'s <c>/Fields</c> array — the
/// number of <em>top-level</em> fields. It is NOT the count of terminal (leaf) fields: a non-terminal
/// field with <c>/Kids</c> contributes one to this count regardless of how many children it owns. For a
/// full terminal-field walk, use <c>PdfDocumentEditor.Forms.Count</c>; this accessor is a cheap existence
/// check (does the document have a form at all?) and a top-level count, not a recursion.
/// </remarks>
public sealed class AcroFormInfo
{
    internal AcroFormInfo(int topLevelFieldCount)
    {
        TopLevelFieldCount = topLevelFieldCount;
    }

    /// <summary>The number of entries in the <c>/AcroForm</c>'s <c>/Fields</c> array. Zero when the
    /// <c>/AcroForm</c> dictionary exists but declares no fields (a malformed/empty form).</summary>
    public int TopLevelFieldCount { get; }

    /// <summary>True iff <see cref="TopLevelFieldCount"/> is greater than zero — the document catalog
    /// references an <c>/AcroForm</c> with at least one declared field.</summary>
    public bool HasFields => TopLevelFieldCount > 0;
}

/// <summary>
/// Reads a document's interactive form (ISO 32000-1, 12.7): the <c>/AcroForm</c> entry of the catalog.
/// Returns null when the document has no <c>/AcroForm</c>. This deliberately duplicates the small catalog
/// walk that <c>Editing.Forms.FormFieldTree</c> performs internally — kept independent (rather than
/// reused) so this read-only accessor on <see cref="PdfDocument"/> does not depend on the Editing layer
/// (mirroring the <c>OutputIntentReader</c> / <c>TagTreeBuilder</c> convention).
/// </summary>
internal static class AcroFormReader
{
    /// <summary>The <c>/AcroForm</c> form descriptor, or null when the catalog has no
    /// <c>/AcroForm</c> entry.</summary>
    public static AcroFormInfo? Read(PdfDocument document)
    {
        PdfDictionary? acroForm = Resolve(document, document.GetCatalog()?.Dictionary.Get("AcroForm"))
            as PdfDictionary;
        if (acroForm is null)
            return null;

        // /Fields may be inline or an indirect reference; absent or non-array → 0 (empty form).
        PdfObject? fieldsRaw = Resolve(document, acroForm.Get("Fields"));
        int count = fieldsRaw is PdfArray fields ? fields.Count : 0;
        return new AcroFormInfo(count);
    }

    private static PdfObject? Resolve(PdfDocument document, PdfObject? obj) =>
        obj is PdfIndirectReference reference ? document.ResolveReference(reference) : obj;
}
