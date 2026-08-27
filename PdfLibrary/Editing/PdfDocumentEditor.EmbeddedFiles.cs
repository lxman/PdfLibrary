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

/// <summary>One filespec <see cref="PdfDocumentEditor.RepairFileSpecNames"/> touched.
/// <paramref name="Name"/> is the TEXT VALUE that was copied from the usable key into the missing one —
/// not an identifier for the filespec, and in particular not the /EmbeddedFiles name-tree key
/// <see cref="FileSpecNameRepairReport.Declined"/> reports (that one comes from
/// <c>IdentifyFileSpec</c>). The two usually coincide, and are free not to.</summary>
public sealed record FileSpecNameRepair(string Name, bool WroteF, bool WroteUf);

/// <summary>What <see cref="PdfDocumentEditor.RepairFileSpecNames"/> did. <paramref name="Declined"/>
/// names each filespec that could not be repaired because it carries no usable source key.</summary>
public sealed record FileSpecNameRepairReport(
    IReadOnlyList<FileSpecNameRepair> Repaired,
    IReadOnlyList<string> Declined);

/// <summary>One filespec <see cref="PdfDocumentEditor.PreviewFileSpecNameRepairs"/> found repairable —
/// same walk and predicate as <see cref="FileSpecNameRepair"/>, but PROSPECTIVE: nothing has been
/// written. Deliberately NOT <see cref="FileSpecNameRepair"/> reused for the preview: that record's
/// <c>WroteF</c>/<c>WroteUf</c> are past tense, accurate for a call that just wrote and wrong for one
/// that did not (see <see cref="PdfDocumentEditor.PreviewFileSpecNameRepairs"/>'s own doc comment for
/// the reasoning). Conditional tense here on purpose. <paramref name="Name"/> carries the same meaning
/// it does on <see cref="FileSpecNameRepair"/>: the text value that WOULD be copied, not an identifier
/// for the filespec.</summary>
public sealed record FileSpecNameRepairCandidate(string Name, bool WouldWriteF, bool WouldWriteUf);

/// <summary>What <see cref="PdfDocumentEditor.PreviewFileSpecNameRepairs"/> found, read-only.
/// <paramref name="Declined"/> carries the exact same meaning as
/// <see cref="FileSpecNameRepairReport.Declined"/> and keeps its name unchanged across both types — it
/// was already a classification ("this filespec has no usable source key"), never an action taken, so
/// unlike <c>Repaired</c>/<c>WouldRepair</c> it needs no tense change to read correctly in either
/// context.</summary>
public sealed record FileSpecNameRepairPreview(
    IReadOnlyList<FileSpecNameRepairCandidate> WouldRepair,
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
        foreach ((string? key, PdfObject value) in EnumerateNameTree(names?.Get("EmbeddedFiles")))
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

    /// <summary>Iterative name-tree walk (key, value) over ANY name tree — the caller supplies the root
    /// node, and nothing here is specific to one tree. Deliberately mirrors the guarded walk in
    /// Document.EmbeddedFileReader (internal to a different concern; not reused so the read path
    /// stays untangled from editing).
    ///
    /// <para>Two callers on two different trees: this file reads the catalog's /Names /EmbeddedFiles
    /// tree, and <c>PdfDocumentEditor.Actions.cs</c> reads /Names /JavaScript for the PDF/A clause 6.5.1
    /// prohibited-action repair. The old name said "embedded files" and was the only thing about this
    /// walk that ever was — keep it tree-agnostic: a third tree should be a third caller, never a third
    /// copy of this walk.</para></summary>
    private IEnumerable<(string? Key, PdfObject Value)> EnumerateNameTree(PdfObject? rootNode)
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

    /// <summary>Fills in whichever of /F, /UF is missing or empty on an embedded-file specification,
    /// copying the one that is usable (ISO 19005-2 6.8 / ISO 14289-1 7.11). Walks the same filespec set
    /// EmbeddedFileSpecRule walks under the same switch: false = catalog /Names /EmbeddedFiles only
    /// (PDF/A), true = also page /Annots[].FS (PDF/UA-1). Writes nothing for a filespec with both keys
    /// already usable, with no /EF, or with no usable (present and non-empty) source key to copy —
    /// otherwise overwrites whichever key is missing or present-but-empty with the other's text.
    ///
    /// <para>Shares its walk (<see cref="EnumerateFileSpecs"/>) and its per-filespec predicate
    /// (<see cref="ClassifyFileSpecName"/>) with the read-only <see cref="PreviewFileSpecNameRepairs"/>,
    /// so the write and the preview can never disagree about what would happen to a given document — the
    /// same factoring <c>TryGetSettableCidFont</c> gives <c>CanSetCidToGidMapIdentity</c>/
    /// <c>SetCidToGidMapIdentity</c> (PdfDocumentEditor.Fonts.cs).</para></summary>
    public FileSpecNameRepairReport RepairFileSpecNames(bool includeAnnotationSpecs)
    {
        var repaired = new List<FileSpecNameRepair>();
        var declined = new List<string>();

        foreach ((PdfDictionary spec, string? nameTreeKey) in EnumerateFileSpecs(includeAnnotationSpecs))
        {
            FileSpecNameOutcome outcome = ClassifyFileSpecName(spec, out bool writeF, out string? text);
            switch (outcome)
            {
                case FileSpecNameOutcome.Declined:
                    declined.Add(IdentifyFileSpec(spec, nameTreeKey));
                    break;

                case FileSpecNameOutcome.Repairable:
                    PdfName targetKey = writeF ? new PdfName("F") : new PdfName("UF");
                    spec.Set(targetKey, PdfString.FromText(text!));
                    repaired.Add(new FileSpecNameRepair(text!, WroteF: writeF, WroteUf: !writeF));
                    break;

                // NotAFileSpec / NothingToDo: nothing to write, nothing to report.
                default:
                    break;
            }
        }

        return new FileSpecNameRepairReport(repaired, declined);
    }

    /// <summary>Read-only preview of exactly what <see cref="RepairFileSpecNames"/> would do RIGHT NOW,
    /// without writing anything — added for <c>EmbeddedFileDomain.Propose</c> (2026-08-21 font-dictionary
    /// and embedded-file remediation, Task 5), which must never call the mutating repair just to learn
    /// its answer. That was the exact defect a prior domain in this same program shipped and had to
    /// redo: staging then undoing left the write already committed to the live session with nothing
    /// tracking it, and a second call afterward hit the write's own idempotency guard and reported a
    /// false refusal for a document that already had the fix. This preview and
    /// <see cref="RepairFileSpecNames"/> share <see cref="EnumerateFileSpecs"/> and
    /// <see cref="ClassifyFileSpecName"/>, so they cannot drift apart the way two independently
    /// maintained copies could.
    ///
    /// <para><b>Naming.</b> <see cref="FileSpecNameRepair"/>'s <c>WroteF</c>/<c>WroteUf</c> are past
    /// tense — correct for a method that just wrote, wrong for one that has not. Rather than reuse that
    /// record here (which would report <c>WroteF: true</c> for a write that never happened),
    /// <see cref="FileSpecNameRepairCandidate"/> uses the conditional <c>WouldWriteF</c>/
    /// <c>WouldWriteUf</c>. <c>Declined</c> keeps its existing name on both reports: it was already a
    /// classification ("this filespec has no usable source key to copy"), never an action taken, so it
    /// reads correctly whether the caller repaired or only looked.</para></summary>
    public FileSpecNameRepairPreview PreviewFileSpecNameRepairs(bool includeAnnotationSpecs)
    {
        var candidates = new List<FileSpecNameRepairCandidate>();
        var declined = new List<string>();

        foreach ((PdfDictionary spec, string? nameTreeKey) in EnumerateFileSpecs(includeAnnotationSpecs))
        {
            FileSpecNameOutcome outcome = ClassifyFileSpecName(spec, out bool writeF, out string? text);
            switch (outcome)
            {
                case FileSpecNameOutcome.Declined:
                    declined.Add(IdentifyFileSpec(spec, nameTreeKey));
                    break;

                case FileSpecNameOutcome.Repairable:
                    candidates.Add(new FileSpecNameRepairCandidate(text!, WouldWriteF: writeF, WouldWriteUf: !writeF));
                    break;

                default:
                    break;
            }
        }

        return new FileSpecNameRepairPreview(candidates, declined);
    }

    /// <summary>Yields every reachable file-spec dictionary once, alongside its name-tree key (null for
    /// one reached only through an annotation) — the single walk <see cref="RepairFileSpecNames"/> and
    /// <see cref="PreviewFileSpecNameRepairs"/> share, so a filespec neither can see is a filespec
    /// neither reports. Mirrors <c>EmbeddedFileSpecRule.CollectFileSpecs</c> (:111-131): same two arms,
    /// same <paramref name="includeAnnotationSpecs"/> switch, same shared `seen` set deduplicating a
    /// filespec reachable from both the name tree and an annotation (legal, if unusual).</summary>
    private IEnumerable<(PdfDictionary Spec, string? NameTreeKey)> EnumerateFileSpecs(bool includeAnnotationSpecs)
    {
        var seen = new HashSet<int>();

        PdfDictionary? catalog = _document.CatalogDictionary;
        PdfDictionary? names = catalog is null ? null : ResolveObject(catalog.Get("Names")) as PdfDictionary;
        foreach ((string? key, PdfObject value) in EnumerateNameTree(names?.Get("EmbeddedFiles")))
        {
            if (ResolveObject(value) is not PdfDictionary spec) continue;
            if (spec.IsIndirect && !seen.Add(spec.ObjectNumber)) continue;
            yield return (spec, key);
        }

        if (!includeAnnotationSpecs) yield break;

        foreach (PdfDictionary page in PageTreeOps.PageDicts(_document))
        {
            if (ResolveObject(page.Get("Annots")) is not PdfArray annots) continue;
            foreach (PdfObject entry in annots)
            {
                if (ResolveObject(entry) is not PdfDictionary annot) continue;
                if (ResolveObject(annot.Get("FS")) is not PdfDictionary spec) continue;
                if (spec.IsIndirect && !seen.Add(spec.ObjectNumber)) continue;
                yield return (spec, null);
            }
        }
    }

    private enum FileSpecNameOutcome { NotAFileSpec, NothingToDo, Repairable, Declined }

    /// <summary>The classification <see cref="RepairFileSpecNames"/> and
    /// <see cref="PreviewFileSpecNameRepairs"/> both act on — the ONLY place the usable/repairable/decline
    /// decision is made, so a write and a preview of the same document state can never disagree. Mirrors
    /// <c>EmbeddedFileSpecRule.Check</c>'s own reading exactly (:41-57): a filespec with no /EF is not an
    /// embedded-file spec at all (<see cref="FileSpecNameOutcome.NotAFileSpec"/>, not declined).
    ///
    /// <para>The decision is keyed on USABILITY, not presence: a key is usable only when it is present
    /// AND its text is non-empty (the same reading <c>EmbeddedFileSpecRule.NonEmpty</c> makes at
    /// :138-139). Presence alone is not enough — <c>/F ()</c> (present, empty) is exactly the shape that
    /// satisfies the PDF/A presence-only test (:51) while still failing PDF/UA-1's non-empty test
    /// (:49-50), so a presence-only "both keys, skip" branch would silently leave a real 7.11 violation
    /// unrepaired AND unreported. Both usable → <see cref="FileSpecNameOutcome.NothingToDo"/>. Neither
    /// usable → <see cref="FileSpecNameOutcome.Declined"/>. Exactly one usable →
    /// <see cref="FileSpecNameOutcome.Repairable"/>, with <paramref name="writeF"/>/<paramref name="text"/>
    /// naming which key a write would target and what it would copy — which may already exist as a
    /// present-but-empty string; the caller overwrites that stale value rather than assuming the target
    /// is absent.</para></summary>
    private FileSpecNameOutcome ClassifyFileSpecName(PdfDictionary spec, out bool writeF, out string? text)
    {
        writeF = false;
        text = null;

        if (ResolveObject(spec.Get("EF")) is not PdfDictionary) return FileSpecNameOutcome.NotAFileSpec;

        string? fText = UsableText(spec, new PdfName("F"));
        string? ufText = UsableText(spec, new PdfName("UF"));
        bool fUsable = fText is not null;
        bool ufUsable = ufText is not null;

        if (fUsable && ufUsable) return FileSpecNameOutcome.NothingToDo;
        if (!fUsable && !ufUsable) return FileSpecNameOutcome.Declined;

        writeF = !fUsable;
        text = writeF ? ufText! : fText!;
        return FileSpecNameOutcome.Repairable;
    }

    /// <summary>The decoded text of <paramref name="spec"/>'s <paramref name="key"/> entry, or null when
    /// the key is absent, not a string, or a string with no content — the single usability test both the
    /// "both usable" skip and the "exactly one usable" repair in <see cref="ClassifyFileSpecName"/> share,
    /// so they cannot disagree on what counts as usable.</summary>
    private string? UsableText(PdfDictionary spec, PdfName key) =>
        ResolveObject(spec.Get(key)) is PdfString s && s.Value.Length > 0 ? s.GetText() : null;

    /// <summary>A stable identifier for a filespec <see cref="ClassifyFileSpecName"/> classified as
    /// <see cref="FileSpecNameOutcome.Declined"/> — consumed verbatim in user-facing warnings by the
    /// remediation layer above this method. Prefers the name-tree key (catalog arm only — an
    /// annotation-reached filespec has none), falls back to /Desc, then "(unnamed)".</summary>
    private string IdentifyFileSpec(PdfDictionary spec, string? nameTreeKey)
    {
        if (!string.IsNullOrEmpty(nameTreeKey)) return nameTreeKey;
        if (ResolveObject(spec.Get("Desc")) is PdfString desc && desc.Value.Length > 0) return desc.GetText();
        return "(unnamed)";
    }
}
