using System.Linq;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Conformance;

/// <summary>
/// Per-run state handed to each <see cref="IConformanceRule"/>: the document under inspection, the
/// profile being targeted, the raw source bytes when available, and shared helpers (indirect-reference
/// resolution, object enumeration) so rules do not each re-implement navigation. Rules read from the
/// document and never mutate it.
/// </summary>
internal sealed class ConformanceContext
{
    private IReadOnlyList<PdfStream>? _streams;

    public ConformanceContext(PdfDocument document, ConformanceProfile target, byte[]? sourceBytes = null)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Target = target;
        SourceBytes = sourceBytes;
    }

    /// <summary>The document being checked.</summary>
    public PdfDocument Document { get; }

    /// <summary>The single profile this run targets.</summary>
    public ConformanceProfile Target { get; }

    /// <summary>
    /// The raw bytes of the source file, or null when the document was inspected in memory (no source
    /// available). Byte-level rules (e.g. post-EOF data) require this and skip gracefully when it is null.
    /// </summary>
    public byte[]? SourceBytes { get; }

    /// <summary>
    /// Every stream object in the document, materialized once and cached. Streams are always indirect,
    /// so enumerating the indirect object table captures them all.
    /// </summary>
    public IReadOnlyList<PdfStream> Streams => _streams ??= CollectStreams();

    /// <summary>
    /// Resolves an indirect reference to its referenced object; returns <paramref name="obj"/>
    /// unchanged when it is already a direct object (or null).
    /// </summary>
    public PdfObject? Resolve(PdfObject? obj) =>
        obj is PdfIndirectReference reference ? Document.ResolveReference(reference) : obj;

    private IReadOnlyList<PdfStream> CollectStreams()
    {
        Document.MaterializeAllObjects();
        return Document.Objects.Values.OfType<PdfStream>().ToList();
    }
}
