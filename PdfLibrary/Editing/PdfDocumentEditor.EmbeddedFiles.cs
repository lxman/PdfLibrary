using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Metadata;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing;

/// <summary>An embedded file's /AFRelationship (ISO 32000-2, 14.13).</summary>
public enum PdfAfRelationship { Unspecified, Source, Data, Alternative, Supplement }

/// <summary>Specification for <see cref="PdfDocumentEditor.AddEmbeddedFile"/>.</summary>
public sealed class PdfEmbeddedFileSpec
{
    /// <summary>Attachment name — the /EmbeddedFiles name-tree key and the filespec /F and /UF.</summary>
    public required string Name { get; init; }

    /// <summary>The file bytes.</summary>
    public required byte[] Data { get; init; }

    /// <summary>MIME type written as the embedded stream's /Subtype (e.g. "text/xml").</summary>
    public string? MimeType { get; init; }

    /// <summary>The filespec /Desc.</summary>
    public string? Description { get; init; }

    /// <summary>The embedded stream's /Params /ModDate.</summary>
    public DateTimeOffset? ModificationDate { get; init; }

    /// <summary>The filespec /AFRelationship, when set.</summary>
    public PdfAfRelationship? Relationship { get; init; }

    /// <summary>Also reference the filespec from the catalog-level /AF associated-files array
    /// (PDF/A-3 requires this for e.g. Factur-X invoices).</summary>
    public bool AssociateWithDocument { get; init; }
}

public sealed partial class PdfDocumentEditor
{
    /// <summary>
    /// Embeds a file: /EmbeddedFile stream + /Filespec, registered in the catalog's
    /// /Names /EmbeddedFiles name tree (created when absent; any existing tree is flattened and
    /// rewritten as a single leaf node with ordinally sorted keys) and, when requested, the
    /// catalog /AF array. An existing entry with the same key (ordinal) is replaced, and its old
    /// filespec is also removed from /AF.
    /// </summary>
    public void AddEmbeddedFile(PdfEmbeddedFileSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        PdfDictionary catalog = _document.CatalogDictionary
            ?? throw new InvalidOperationException("The document has no catalog.");

        // 1. /EmbeddedFile stream
        var efStreamDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("EmbeddedFile"),
        };
        if (spec.MimeType is not null)
            efStreamDict[new PdfName("Subtype")] = new PdfName(spec.MimeType);
        var paramsDict = new PdfDictionary
        {
            [new PdfName("Size")] = new PdfInteger(spec.Data.Length),
        };
        if (spec.ModificationDate is { } mod)
            paramsDict[new PdfName("ModDate")] = PdfString.FromByteLiteral(PdfDate.FormatPdf(mod));
        efStreamDict[new PdfName("Params")] = paramsDict;
        PdfIndirectReference streamRef = _document.RegisterObject(new PdfStream(efStreamDict, spec.Data));

        // 2. /Filespec
        var efDict = new PdfDictionary
        {
            [new PdfName("F")] = streamRef,
            [new PdfName("UF")] = streamRef,
        };
        var filespec = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Filespec"),
            [new PdfName("F")] = PdfString.FromText(spec.Name),
            [new PdfName("UF")] = PdfString.FromText(spec.Name),
            [new PdfName("EF")] = efDict,
        };
        if (spec.Description is not null)
            filespec[new PdfName("Desc")] = PdfString.FromText(spec.Description);
        if (spec.Relationship is { } rel)
            filespec[new PdfName("AFRelationship")] = new PdfName(rel.ToString());
        PdfIndirectReference specRef = _document.RegisterObject(filespec);

        // 3. Rebuild the /EmbeddedFiles name tree: existing entries (flattened) minus same-key,
        //    plus the new one, sorted ordinally, as a single leaf node.
        var replacedSpecs = new HashSet<int>();
        var entries = new List<(string Key, PdfObject Value)>();
        PdfDictionary? names = ResolveObject(catalog.Get("Names")) as PdfDictionary;
        foreach ((string? key, PdfObject value) in EnumerateEmbeddedFilesTree(names?.Get("EmbeddedFiles")))
        {
            if (key is null) continue;
            if (string.Equals(key, spec.Name, StringComparison.Ordinal))
            {
                if (ResolveObject(value) is PdfDictionary { IsIndirect: true } old)
                    replacedSpecs.Add(old.ObjectNumber);
                continue;
            }
            entries.Add((key, value));
        }
        entries.Add((spec.Name, specRef));
        entries.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

        var namesArray = new PdfArray();
        foreach ((string key, PdfObject value) in entries)
        {
            namesArray.Add(PdfString.FromText(key));
            namesArray.Add(value);
        }
        var leaf = new PdfDictionary { [new PdfName("Names")] = namesArray };
        if (names is null)
        {
            names = new PdfDictionary();
            catalog[new PdfName("Names")] = names;
        }
        names[new PdfName("EmbeddedFiles")] = leaf;

        // 4. Catalog /AF: drop refs to any replaced filespec, append the new one when associated.
        var af = new PdfArray();
        if (ResolveObject(catalog.Get("AF")) is PdfArray existingAf)
            foreach (PdfObject entry in existingAf)
                if (ResolveObject(entry) is not PdfDictionary { IsIndirect: true } d || !replacedSpecs.Contains(d.ObjectNumber))
                    af.Add(entry);
        if (spec.AssociateWithDocument)
            af.Add(specRef);
        if (af.Count > 0)
            catalog[new PdfName("AF")] = af;
        else
            catalog.Remove(new PdfName("AF"));
    }

    /// <summary>Iterative name-tree walk (key, value) — deliberately mirrors the guarded walk in
    /// Document.EmbeddedFileReader (internal to a different concern; not reused so the read path
    /// stays untangled from editing).</summary>
    private IEnumerable<(string? Key, PdfObject Value)> EnumerateEmbeddedFilesTree(PdfObject? rootNode)
    {
        var visited = new HashSet<int>();
        var stack = new Stack<PdfObject?>();
        stack.Push(rootNode);
        for (int budget = 100_000; stack.Count > 0 && budget > 0; budget--)
        {
            if (ResolveObject(stack.Pop()) is not PdfDictionary node)
                continue;
            if (node.IsIndirect && !visited.Add(node.ObjectNumber))
                continue;
            if (ResolveObject(node.Get("Names")) is PdfArray leafEntries)
                for (int i = 1; i < leafEntries.Count; i += 2)
                    yield return ((ResolveObject(leafEntries[i - 1]) as PdfString)?.GetText(), leafEntries[i]);
            if (ResolveObject(node.Get("Kids")) is PdfArray kids)
                foreach (PdfObject kid in kids)
                    stack.Push(kid);
        }
    }

    private PdfObject? ResolveObject(PdfObject? obj) =>
        obj is PdfIndirectReference reference ? _document.ResolveReference(reference) : obj;
}
