using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Conformance;

/// <summary>
/// Per-run state handed to each <see cref="IConformanceRule"/>: the document under inspection, the
/// profile being targeted, and shared helpers (indirect-reference resolution) so rules do not each
/// re-implement navigation. Rules read from the document and never mutate it.
/// </summary>
internal sealed class ConformanceContext
{
    public ConformanceContext(PdfDocument document, ConformanceProfile target)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Target = target;
    }

    /// <summary>The document being checked.</summary>
    public PdfDocument Document { get; }

    /// <summary>The single profile this run targets.</summary>
    public ConformanceProfile Target { get; }

    /// <summary>
    /// Resolves an indirect reference to its referenced object; returns <paramref name="obj"/>
    /// unchanged when it is already a direct object (or null).
    /// </summary>
    public PdfObject? Resolve(PdfObject? obj) =>
        obj is PdfIndirectReference reference ? Document.ResolveReference(reference) : obj;
}
