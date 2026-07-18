using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Document;

/// <summary>
/// A read-only view of one embedded file: an entry of the catalog's <c>/Names /EmbeddedFiles</c> name
/// tree, or a catalog-level <c>/AF</c> associated file (ISO 32000-2, 7.11.4 / 14.13). Built with
/// <see cref="PdfDocument.GetEmbeddedFiles"/>.
/// </summary>
public sealed class EmbeddedFileDescriptor
{
    private readonly byte[]? _data;

    internal EmbeddedFileDescriptor(
        string? name, string? fileName, string? unicodeFileName, string? description,
        string? afRelationship, string? mimeType, bool isAssociated, byte[]? data)
    {
        Name = name;
        FileName = fileName;
        UnicodeFileName = unicodeFileName;
        Description = description;
        AfRelationship = afRelationship;
        MimeType = mimeType;
        IsAssociated = isAssociated;
        _data = data;
    }

    /// <summary>The entry's key in the <c>/EmbeddedFiles</c> name tree (what viewers list), or null for a
    /// file reachable only through the catalog's <c>/AF</c> array.</summary>
    public string? Name { get; }

    /// <summary>The file specification's <c>/F</c> file name, or null when absent.</summary>
    public string? FileName { get; }

    /// <summary>The file specification's <c>/UF</c> Unicode file name, or null when absent.</summary>
    public string? UnicodeFileName { get; }

    /// <summary>The file specification's <c>/Desc</c> description, or null when absent.</summary>
    public string? Description { get; }

    /// <summary>The file specification's <c>/AFRelationship</c> name (e.g. <c>"Alternative"</c>,
    /// <c>"Data"</c>, <c>"Source"</c>; ISO 32000-2, 14.13), or null when absent.</summary>
    public string? AfRelationship { get; }

    /// <summary>The embedded file stream's <c>/Subtype</c> MIME type (e.g. <c>"text/xml"</c>), or null
    /// when the stream or the key is absent.</summary>
    public string? MimeType { get; }

    /// <summary>True iff the file specification is referenced from the document catalog's <c>/AF</c>
    /// associated-files array (ISO 32000-2, 14.13) — as PDF/A-3 requires for e.g. Factur-X invoices.</summary>
    public bool IsAssociated { get; }

    /// <summary>True iff the embedded file stream resolved and its data decoded.</summary>
    public bool HasData => _data is not null;

    /// <summary>A defensive copy of the decoded embedded file bytes, or null when
    /// <see cref="HasData"/> is false.</summary>
    public byte[]? GetDataBytes() => _data is null ? null : (byte[])_data.Clone();
}

/// <summary>
/// Reads a document's embedded files — the catalog's <c>/Names /EmbeddedFiles</c> name tree plus
/// catalog-level <c>/AF</c> associated files — into public <see cref="EmbeddedFileDescriptor"/>s.
/// This deliberately duplicates the small catalog/name-tree walk that
/// <c>Conformance.ConformanceContext</c> performs internally — kept independent (rather than reused)
/// so this public reader never risks perturbing the load-bearing conformance suite.
/// Malformed content never throws: a failing entry degrades to metadata-only
/// (<see cref="EmbeddedFileDescriptor.HasData"/> = false) and junk trees yield what was reachable.
/// </summary>
internal static class EmbeddedFileReader
{
    public static IReadOnlyList<EmbeddedFileDescriptor> Read(PdfDocument document)
    {
        var result = new List<EmbeddedFileDescriptor>();
        PdfDictionary? catalog = document.GetCatalog()?.Dictionary;
        if (catalog is null)
            return result;

        // Object numbers of file specs the catalog's /AF array marks as associated files.
        var associated = new HashSet<int>();
        if (Resolve(document, catalog.Get("AF")) is PdfArray af)
            foreach (PdfObject entry in af)
                if (Resolve(document, entry) is PdfDictionary { IsIndirect: true } afSpec)
                    associated.Add(afSpec.ObjectNumber);

        // Primary registry: the /Names /EmbeddedFiles name tree.
        var yielded = new HashSet<int>();
        if (Resolve(document, catalog.Get("Names")) is PdfDictionary names)
        {
            foreach ((string? name, PdfObject value) in EnumerateNameTree(document, names.Get("EmbeddedFiles")))
            {
                if (Resolve(document, value) is not PdfDictionary spec)
                    continue;
                if (spec.IsIndirect)
                    yielded.Add(spec.ObjectNumber);
                bool isAssociated = spec.IsIndirect && associated.Contains(spec.ObjectNumber);
                result.Add(Describe(document, spec, name, isAssociated));
            }
        }

        // Union: catalog /AF specs the name tree did not already yield (a Factur-X file references the
        // SAME spec from both places — that must stay one descriptor, carrying its name-tree identity).
        if (Resolve(document, catalog.Get("AF")) is PdfArray afArray)
            foreach (PdfObject entry in afArray)
                if (Resolve(document, entry) is PdfDictionary { IsIndirect: true } spec
                    && !yielded.Contains(spec.ObjectNumber))
                {
                    yielded.Add(spec.ObjectNumber);
                    result.Add(Describe(document, spec, name: null, isAssociated: true));
                }

        return result;
    }

    /// <summary>One descriptor from a /Filespec dictionary: metadata keys plus — when the /EF stream
    /// resolves and decodes — the file bytes. Decode failures degrade to HasData = false.</summary>
    private static EmbeddedFileDescriptor Describe(
        PdfDocument document, PdfDictionary spec, string? name, bool isAssociated)
    {
        string? fileName = TextValue(document, spec.Get("F"));
        string? unicodeFileName = TextValue(document, spec.Get("UF"));
        string? description = TextValue(document, spec.Get("Desc"));
        string? afRelationship = (Resolve(document, spec.Get("AFRelationship")) as PdfName)?.Value;

        string? mimeType = null;
        byte[]? data = null;
        if (Resolve(document, spec.Get("EF")) is PdfDictionary ef)
        {
            // Prefer the /UF stream, fall back to /F — the same preference the conformance rule uses.
            PdfStream? stream = Resolve(document, ef.Get("UF")) as PdfStream
                ?? Resolve(document, ef.Get("F")) as PdfStream;
            if (stream is not null)
            {
                mimeType = (Resolve(document, stream.Dictionary.Get("Subtype")) as PdfName)?.Value;
                try
                {
                    data = stream.GetDecodedData(document.Decryptor);
                }
                catch (Exception)
                {
                    // Unknown/broken filter, truncated data … — report the entry without bytes.
                    data = null;
                }
            }
        }

        return new EmbeddedFileDescriptor(
            name, fileName, unicodeFileName, description, afRelationship, mimeType, isAssociated, data);
    }

    /// <summary>
    /// Iterative name-tree walk yielding (key, value) pairs: leaf <c>/Names</c> arrays are flat
    /// <c>[key1 value1 key2 value2 …]</c>; intermediate nodes descend through <c>/Kids</c>. Guarded
    /// against indirect-node cycles and unboundedly deep/hostile trees (node budget), mirroring
    /// <c>ConformanceContext.EnumerateNameTree</c>.
    /// </summary>
    private static IEnumerable<(string? Name, PdfObject Value)> EnumerateNameTree(
        PdfDocument document, PdfObject? rootNode)
    {
        var visited = new HashSet<int>();
        var stack = new Stack<PdfObject?>();
        stack.Push(rootNode);

        for (int budget = 100_000; stack.Count > 0 && budget > 0; budget--)
        {
            if (Resolve(document, stack.Pop()) is not PdfDictionary node)
                continue;
            if (node.IsIndirect && !visited.Add(node.ObjectNumber))
                continue; // guards indirect-node cycles

            if (Resolve(document, node.Get("Names")) is PdfArray entries)
                for (int i = 1; i < entries.Count; i += 2)
                    yield return ((Resolve(document, entries[i - 1]) as PdfString)?.GetText(), entries[i]);

            if (Resolve(document, node.Get("Kids")) is PdfArray kids)
                foreach (PdfObject kid in kids)
                    stack.Push(kid);
        }
    }

    private static string? TextValue(PdfDocument document, PdfObject? obj) =>
        Resolve(document, obj) is PdfString s ? s.GetText() : null;

    private static PdfObject? Resolve(PdfDocument document, PdfObject? obj) =>
        obj is PdfIndirectReference reference ? document.ResolveReference(reference) : obj;
}
