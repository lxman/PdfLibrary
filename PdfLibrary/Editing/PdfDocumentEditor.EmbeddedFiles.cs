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

    /// <summary>The filespec /AFRelationship, when set. When null and
    /// <see cref="AssociateWithDocument"/> is true, defaults to
    /// <see cref="PdfAfRelationship.Unspecified"/> — ISO 19005-3 §6.8 requires every file
    /// referenced from the catalog /AF array to carry /AFRelationship. Has no effect (no key is
    /// written) when null and <see cref="AssociateWithDocument"/> is false.</summary>
    public PdfAfRelationship? Relationship { get; init; }

    /// <summary>Also reference the filespec from the catalog-level /AF associated-files array
    /// (PDF/A-3 requires this for e.g. Factur-X invoices). When true and <see cref="Relationship"/>
    /// is null, the filespec's /AFRelationship defaults to <see cref="PdfAfRelationship.Unspecified"/>
    /// so the ISO 19005-3 §6.8 requirement is still satisfied.</summary>
    public bool AssociateWithDocument { get; init; }
}

/// <summary>One filespec <see cref="PdfDocumentEditor.RepairFileSpecNames"/> touched.</summary>
public sealed record FileSpecNameRepair(string Name, bool WroteF, bool WroteUf);

/// <summary>What <see cref="PdfDocumentEditor.RepairFileSpecNames"/> did. <paramref name="Declined"/>
/// names each filespec that could not be repaired because it carries no usable source key.</summary>
public sealed record FileSpecNameRepairReport(
    IReadOnlyList<FileSpecNameRepair> Repaired,
    IReadOnlyList<string> Declined);

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
        // ISO 19005-3 §6.8: every filespec referenced from the catalog /AF array shall carry
        // /AFRelationship. When associated with no explicit relationship, default to Unspecified
        // rather than silently omitting the key.
        PdfAfRelationship? effectiveRelationship = spec.Relationship
            ?? (spec.AssociateWithDocument ? PdfAfRelationship.Unspecified : null);
        if (effectiveRelationship is { } rel)
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

    /// <summary>Fills in whichever of /F, /UF is missing on an embedded-file specification, copying the
    /// one that is present (ISO 19005-2 6.8 / ISO 14289-1 7.11). Walks the same filespec set
    /// EmbeddedFileSpecRule walks under the same switch: false = catalog /Names /EmbeddedFiles only
    /// (PDF/A), true = also page /Annots[].FS (PDF/UA-1). Writes nothing for a filespec with both keys,
    /// with no /EF, or with no non-empty source key to copy.</summary>
    public FileSpecNameRepairReport RepairFileSpecNames(bool includeAnnotationSpecs)
    {
        var repaired = new List<FileSpecNameRepair>();
        var declined = new List<string>();
        // Shared across both arms so a filespec reachable from BOTH the name tree and an annotation
        // (legal, if unusual) is repaired once, mirroring EmbeddedFileSpecRule.CollectFileSpecs's own
        // single `seen` set (:113, shared across its two loops too).
        var seen = new HashSet<int>();

        PdfDictionary? catalog = _document.CatalogDictionary;
        PdfDictionary? names = catalog is null ? null : ResolveObject(catalog.Get("Names")) as PdfDictionary;
        foreach ((string? key, PdfObject value) in EnumerateEmbeddedFilesTree(names?.Get("EmbeddedFiles")))
        {
            if (ResolveObject(value) is not PdfDictionary spec) continue;
            if (spec.IsIndirect && !seen.Add(spec.ObjectNumber)) continue;
            RepairFileSpecName(spec, key, repaired, declined);
        }

        if (includeAnnotationSpecs)
        {
            foreach (PdfDictionary page in PageTreeOps.PageDicts(_document))
            {
                if (ResolveObject(page.Get("Annots")) is not PdfArray annots) continue;
                foreach (PdfObject entry in annots)
                {
                    if (ResolveObject(entry) is not PdfDictionary annot) continue;
                    if (ResolveObject(annot.Get("FS")) is not PdfDictionary spec) continue;
                    if (spec.IsIndirect && !seen.Add(spec.ObjectNumber)) continue;
                    RepairFileSpecName(spec, nameTreeKey: null, repaired, declined);
                }
            }
        }

        return new FileSpecNameRepairReport(repaired, declined);
    }

    /// <summary>One filespec's worth of <see cref="RepairFileSpecNames"/> — shared by the catalog and
    /// annotation arms so they can never disagree on the predicate. Mirrors
    /// <c>EmbeddedFileSpecRule.Check</c>'s own reading exactly (:41-57): a filespec with no /EF is not an
    /// embedded-file spec at all (skipped, not declined); one that already carries both /F and /UF needs
    /// no repair (skipped, not declined — satisfies the PDF/A presence-only test already); a source key is
    /// usable only when it is present AND its text is non-empty, because copying an empty value would
    /// still fail the PDF/UA-1 non-empty test (:138-139) even though the PDF/A presence test would call it
    /// fixed.</summary>
    private void RepairFileSpecName(
        PdfDictionary spec, string? nameTreeKey, List<FileSpecNameRepair> repaired, List<string> declined)
    {
        var fKey = new PdfName("F");
        var ufKey = new PdfName("UF");

        if (ResolveObject(spec.Get("EF")) is not PdfDictionary) return; // not an embedded-file spec

        bool fPresent = spec.ContainsKey(fKey);
        bool ufPresent = spec.ContainsKey(ufKey);
        if (fPresent && ufPresent) return; // nothing missing

        if (!fPresent && !ufPresent)
        {
            declined.Add(IdentifyFileSpec(spec, nameTreeKey));
            return;
        }

        PdfName sourceKey = fPresent ? fKey : ufKey;
        PdfName targetKey = fPresent ? ufKey : fKey;

        if (ResolveObject(spec.Get(sourceKey)) is not PdfString source || source.Value.Length == 0)
        {
            declined.Add(IdentifyFileSpec(spec, nameTreeKey));
            return;
        }

        string text = source.GetText();
        spec.Set(targetKey, PdfString.FromText(text));
        repaired.Add(new FileSpecNameRepair(text, WroteF: !fPresent, WroteUf: !ufPresent));
    }

    /// <summary>A stable identifier for a filespec <see cref="RepairFileSpecName"/> could not repair —
    /// consumed verbatim in user-facing warnings by the remediation layer above this method. Prefers the
    /// name-tree key (catalog arm only — an annotation-reached filespec has none), falls back to /Desc,
    /// then "(unnamed)".</summary>
    private string IdentifyFileSpec(PdfDictionary spec, string? nameTreeKey)
    {
        if (!string.IsNullOrEmpty(nameTreeKey)) return nameTreeKey;
        if (ResolveObject(spec.Get("Desc")) is PdfString desc && desc.Value.Length > 0) return desc.GetText();
        return "(unnamed)";
    }
}
