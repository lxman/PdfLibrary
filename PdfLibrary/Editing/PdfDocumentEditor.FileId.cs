using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Editing;

public sealed partial class PdfDocumentEditor
{
    /// <summary>
    /// Sets the trailer's <c>/ID</c> entry to a two-element array built from
    /// <paramref name="idBytes"/>, using the SAME bytes for both elements. This is the public
    /// surface for the mechanism <c>ResaveVerificationTests.
    /// FileId_finding_is_cleared_when_the_caller_sets_trailer_id_before_saving</c> proves clears
    /// the <c>file-id</c> conformance finding on save: <c>PdfDocumentSerializer</c> only ever
    /// propagates an existing <see cref="PdfLibrary.Structure.PdfDocument"/> trailer id — it never
    /// mints one — so a document with no <c>/ID</c> needs a caller to set it before saving.
    /// </summary>
    /// <remarks>
    /// <see cref="PdfLibrary.Structure.PdfDocument.Trailer"/> and the primitive types that back it
    /// (<c>PdfTrailer</c>, <c>PdfString</c>, <c>PdfArray</c>) are all `internal` to this assembly,
    /// so callers outside it (e.g. Pellucid.Core's remediation domains) cannot reach
    /// <c>editor.Document.Trailer.Id = new PdfArray(...)</c> directly. This wrapper exists for
    /// exactly that caller; it does not change what a plain resave does on its own.
    /// </remarks>
    public void SetFileId(byte[] idBytes)
    {
        ArgumentNullException.ThrowIfNull(idBytes);
        var value = new PdfString(idBytes);
        _document.Trailer.Id = new PdfArray(value, value);
    }
}
