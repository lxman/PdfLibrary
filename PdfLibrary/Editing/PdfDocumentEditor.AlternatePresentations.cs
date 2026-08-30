using PdfLibrary.Conformance;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Editing;

/// <summary>The two PDF/A clause 6.10 hosts that can activate alternate presentation behavior.</summary>
public enum AlternatePresentationsOwnerKind { NameDictionary, Page }

/// <summary>One host entry that can be removed with explicit presentation-loss consent.</summary>
public sealed record AlternatePresentationsRepairCandidate(
    AlternatePresentationsOwnerKind OwnerKind,
    int? ObjectNumber,
    int? PageIndex,
    int? StructureObjectNumber);

/// <summary>One host entry deliberately left in place.</summary>
public sealed record AlternatePresentationsRepairRefusal(
    AlternatePresentationsOwnerKind OwnerKind,
    int? ObjectNumber,
    int? PageIndex,
    int? StructureObjectNumber,
    string Reason);

/// <summary>Read-only classification of every alternate-presentations host entry.</summary>
public sealed record AlternatePresentationsRepairPreview(
    IReadOnlyList<AlternatePresentationsRepairCandidate> Candidates,
    IReadOnlyList<AlternatePresentationsRepairRefusal> Refused);

/// <summary>One name-dictionary or page host key removed by the repair.</summary>
public sealed record AlternatePresentationsRepair(
    AlternatePresentationsOwnerKind OwnerKind,
    int? ObjectNumber,
    int? PageIndex,
    int? StructureObjectNumber);

/// <summary>What a document-scoped alternate-presentations repair changed and refused.</summary>
public sealed record AlternatePresentationsRepairReport(
    IReadOnlyList<AlternatePresentationsRepair> Repaired,
    IReadOnlyList<AlternatePresentationsRepairRefusal> Refused);

public sealed partial class PdfDocumentEditor
{
    private static readonly PdfName AlternatePresentationsKey = new("AlternatePresentations");
    private static readonly PdfName PresentationStepsKey = new("PresSteps");

    private sealed record AlternatePresentationsHost(
        PdfDictionary Dictionary,
        PdfName Key,
        AlternatePresentationsRepairCandidate Candidate);

    private sealed record AlternatePresentationsClassification(
        IReadOnlyList<AlternatePresentationsHost> Candidates,
        IReadOnlyList<AlternatePresentationsRepairRefusal> Refused);

    /// <summary>
    /// Classifies the document name-dictionary <c>/AlternatePresentations</c> entry and every recursively
    /// enumerated page <c>/PresSteps</c> entry without writing. The host key is the prohibited construct;
    /// detaching it never mutates a direct or indirect slideshow name tree, navigation-node graph, action,
    /// transition, optional-content group, or other object that another host may share.
    /// </summary>
    public AlternatePresentationsRepairPreview PreviewAlternatePresentationsRepair()
    {
        AlternatePresentationsClassification classified = ClassifyAlternatePresentationsRepair();
        return new AlternatePresentationsRepairPreview(
            [.. classified.Candidates.Select(item => item.Candidate)], classified.Refused);
    }

    /// <summary>
    /// Removes every current <c>/AlternatePresentations</c> and <c>/PresSteps</c> host key. This is a
    /// document-scoped operation because the conformance finding does not identify the name-dictionary
    /// host and partial removal leaves the same rule open. Signed and DocMDP-protected documents refuse
    /// because Pellucid performs a full rewrite rather than a signature-preserving append.
    /// </summary>
    public AlternatePresentationsRepairReport RepairAlternatePresentations()
    {
        AlternatePresentationsClassification classified = ClassifyAlternatePresentationsRepair();
        var repaired = new List<AlternatePresentationsRepair>();

        foreach (AlternatePresentationsHost host in classified.Candidates)
        {
            if (!host.Dictionary.Remove(host.Key))
                continue;

            AlternatePresentationsRepairCandidate candidate = host.Candidate;
            repaired.Add(new AlternatePresentationsRepair(
                candidate.OwnerKind,
                candidate.ObjectNumber,
                candidate.PageIndex,
                candidate.StructureObjectNumber));
        }

        return new AlternatePresentationsRepairReport(repaired, classified.Refused);
    }

    private AlternatePresentationsClassification ClassifyAlternatePresentationsRepair()
    {
        var context = new ConformanceContext(_document, ConformanceProfile.PdfA2b);
        var hosts = new List<AlternatePresentationsHost>();

        if (ResolveObject(_document.CatalogDictionary?.Get("Names")) is PdfDictionary names)
            AddHost(names, AlternatePresentationsKey, AlternatePresentationsOwnerKind.NameDictionary, null);

        var pageIndex = 0;
        foreach (PdfPage page in _document.GetPages())
        {
            AddHost(page.Dictionary, PresentationStepsKey, AlternatePresentationsOwnerKind.Page, pageIndex);
            pageIndex++;
        }

        if (hosts.Count == 0)
            return new AlternatePresentationsClassification([], []);

        if (!HasDocMdp(context) && !HasSignedSignature(context))
            return new AlternatePresentationsClassification(hosts, []);

        const string reason =
            "Alternate presentations and page presentation steps were left in place because this document "
          + "carries a signed signature or DocMDP permission. Pellucid rewrites the file rather than "
          + "appending a signature-preserving revision, so removing the presentation behavior would "
          + "invalidate that protection.";

        return new AlternatePresentationsClassification(
            [],
            [.. hosts.Select(host => new AlternatePresentationsRepairRefusal(
                host.Candidate.OwnerKind,
                host.Candidate.ObjectNumber,
                host.Candidate.PageIndex,
                host.Candidate.StructureObjectNumber,
                reason))]);

        void AddHost(PdfDictionary dictionary, PdfName key, AlternatePresentationsOwnerKind kind, int? pageIndex)
        {
            if (!dictionary.ContainsKey(key))
                return;

            PdfObject? structure = ResolveObject(dictionary.Get(key));
            var candidate = new AlternatePresentationsRepairCandidate(
                kind,
                dictionary.IsIndirect ? dictionary.ObjectNumber : null,
                pageIndex,
                structure is { IsIndirect: true } ? structure.ObjectNumber : null);
            hosts.Add(new AlternatePresentationsHost(dictionary, key, candidate));
        }
    }
}
