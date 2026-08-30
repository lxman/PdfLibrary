using PdfLibrary.Conformance;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Editing;

/// <summary>The two PDF/A clause 6.5.2 hosts whose <c>/AA</c> entry is prohibited.</summary>
public enum AdditionalActionsOwnerKind { Catalog, Page }

/// <summary>One catalog or page <c>/AA</c> entry that can be removed with explicit consent.</summary>
public sealed record AdditionalActionsRepairCandidate(
    AdditionalActionsOwnerKind OwnerKind,
    int? ObjectNumber,
    int? PageIndex,
    IReadOnlyList<string> TriggerKeys);

/// <summary>One catalog or page <c>/AA</c> entry deliberately left in place.</summary>
public sealed record AdditionalActionsRepairRefusal(
    AdditionalActionsOwnerKind OwnerKind,
    int? ObjectNumber,
    int? PageIndex,
    string Reason);

/// <summary>Read-only classification of every catalog and page <c>/AA</c> entry.</summary>
public sealed record AdditionalActionsRepairPreview(
    IReadOnlyList<AdditionalActionsRepairCandidate> Candidates,
    IReadOnlyList<AdditionalActionsRepairRefusal> Refused);

/// <summary>One catalog or page <c>/AA</c> host key removed by the repair.</summary>
public sealed record AdditionalActionsRepair(
    AdditionalActionsOwnerKind OwnerKind,
    int? ObjectNumber,
    int? PageIndex,
    IReadOnlyList<string> TriggerKeys);

/// <summary>What a document-scoped additional-actions repair changed and refused.</summary>
public sealed record AdditionalActionsRepairReport(
    IReadOnlyList<AdditionalActionsRepair> Repaired,
    IReadOnlyList<AdditionalActionsRepairRefusal> Refused);

public sealed partial class PdfDocumentEditor
{
    private sealed record AdditionalActionsHost(
        PdfDictionary Dictionary,
        AdditionalActionsRepairCandidate Candidate);

    private sealed record AdditionalActionsClassification(
        IReadOnlyList<AdditionalActionsHost> Candidates,
        IReadOnlyList<AdditionalActionsRepairRefusal> Refused);

    /// <summary>
    /// Classifies every catalog and recursively enumerated page <c>/AA</c> entry without writing.
    /// The host key is the prohibited construct, so direct and indirect action dictionaries have the
    /// same outcome: removal detaches the complete trigger action, including every reachable
    /// <c>/Next</c> action, without mutating an action dictionary that another host may share.
    /// </summary>
    public AdditionalActionsRepairPreview PreviewAdditionalActionsRepair()
    {
        AdditionalActionsClassification classified = ClassifyAdditionalActionsRepair();
        return new AdditionalActionsRepairPreview(
            [.. classified.Candidates.Select(item => item.Candidate)], classified.Refused);
    }

    /// <summary>
    /// Removes every current catalog and page <c>/AA</c> host key. This is deliberately document-scoped:
    /// the conformance findings do not expose host object numbers, and partial removal would leave the
    /// same rule open. Documents carrying a signed signature or DocMDP permission are refused because
    /// Pellucid performs a full rewrite rather than a signature-preserving append.
    /// </summary>
    public AdditionalActionsRepairReport RepairAdditionalActions()
    {
        AdditionalActionsClassification classified = ClassifyAdditionalActionsRepair();
        var repaired = new List<AdditionalActionsRepair>();

        foreach (AdditionalActionsHost host in classified.Candidates)
        {
            if (!host.Dictionary.Remove(AdditionalActionsKey))
                continue;

            AdditionalActionsRepairCandidate candidate = host.Candidate;
            repaired.Add(new AdditionalActionsRepair(
                candidate.OwnerKind,
                candidate.ObjectNumber,
                candidate.PageIndex,
                candidate.TriggerKeys));
        }

        return new AdditionalActionsRepairReport(repaired, classified.Refused);
    }

    private AdditionalActionsClassification ClassifyAdditionalActionsRepair()
    {
        var context = new ConformanceContext(_document, ConformanceProfile.PdfA2b);
        var hosts = new List<AdditionalActionsHost>();
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        AddHost(_document.CatalogDictionary, AdditionalActionsOwnerKind.Catalog, pageIndex: null);

        var pageIndex = 0;
        foreach (PdfPage page in _document.GetPages())
        {
            AddHost(page.Dictionary, AdditionalActionsOwnerKind.Page, pageIndex);
            pageIndex++;
        }

        if (hosts.Count == 0)
            return new AdditionalActionsClassification([], []);

        if (!HasDocMdp(context) && !HasSignedSignature(context))
            return new AdditionalActionsClassification(hosts, []);

        const string reason =
            "Catalog and page additional actions were left in place because this document carries a "
          + "signed signature or DocMDP permission. Pellucid rewrites the file rather than appending a "
          + "signature-preserving revision, so removing the actions would invalidate that protection.";

        return new AdditionalActionsClassification(
            [],
            [.. hosts.Select(host => new AdditionalActionsRepairRefusal(
                host.Candidate.OwnerKind,
                host.Candidate.ObjectNumber,
                host.Candidate.PageIndex,
                reason))]);

        void AddHost(PdfDictionary? dictionary, AdditionalActionsOwnerKind kind, int? pageIndex)
        {
            if (dictionary is null || !dictionary.ContainsKey(AdditionalActionsKey) || !seen.Add(dictionary))
                return;

            IReadOnlyList<string> triggerKeys =
                ResolveObject(dictionary.Get(AdditionalActionsKey)) is PdfDictionary triggers
                    ? [.. triggers.Keys.Select(key => key.Value).Order(StringComparer.Ordinal)]
                    : [];
            hosts.Add(new AdditionalActionsHost(
                dictionary,
                new AdditionalActionsRepairCandidate(
                    kind,
                    dictionary.IsIndirect ? dictionary.ObjectNumber : null,
                    pageIndex,
                    triggerKeys)));
        }
    }
}
