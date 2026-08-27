using PdfLibrary.Conformance;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Editing;

/// <summary>The roles through which one dictionary violates PDF/A's form-action restriction.</summary>
public enum FormFieldActionOwnerKind { Widget, Field, MergedWidgetField }

/// <summary>One indirect host whose prohibited form-action entries can be removed atomically.</summary>
public sealed record FormFieldActionRepairCandidate(
    int ObjectNumber,
    FormFieldActionOwnerKind OwnerKind,
    bool RemovesAction,
    bool RemovesAdditionalActions);

/// <summary>An offending host that this repair deliberately leaves unchanged.</summary>
public sealed record FormFieldActionRepairRefusal(
    int? ObjectNumber,
    FormFieldActionOwnerKind OwnerKind,
    string Reason);

/// <summary>Read-only result of classifying every form-action host in the document.</summary>
public sealed record FormFieldActionRepairPreview(
    IReadOnlyList<FormFieldActionRepairCandidate> Candidates,
    IReadOnlyList<FormFieldActionRepairRefusal> Refused);

/// <summary>The host entries actually removed by one repair.</summary>
public sealed record FormFieldActionRepair(
    int ObjectNumber,
    FormFieldActionOwnerKind OwnerKind,
    bool RemovedAction,
    bool RemovedAdditionalActions);

/// <summary>What an exact form-action repair selection changed and refused.</summary>
public sealed record FormFieldActionRepairReport(
    IReadOnlyList<FormFieldActionRepair> Repaired,
    IReadOnlyList<FormFieldActionRepairRefusal> Refused);

public sealed partial class PdfDocumentEditor
{
    private sealed class FormFieldActionHost(PdfDictionary dictionary)
    {
        public PdfDictionary Dictionary { get; } = dictionary;
        public bool IsWidget { get; set; }
        public bool IsField { get; set; }
        public bool RemovesAction { get; set; }
        public bool RemovesAdditionalActions { get; set; }

        public FormFieldActionOwnerKind OwnerKind => (IsWidget, IsField) switch
        {
            (true, true) => FormFieldActionOwnerKind.MergedWidgetField,
            (true, false) => FormFieldActionOwnerKind.Widget,
            _ => FormFieldActionOwnerKind.Field,
        };
    }

    private sealed record FormFieldActionClassification(
        IReadOnlyList<(FormFieldActionHost Host, FormFieldActionRepairCandidate Candidate)> Candidates,
        IReadOnlyList<FormFieldActionRepairRefusal> Refused);

    /// <summary>
    /// Classifies PDF/A form-action hosts without writing. Widget <c>/A</c>/<c>/AA</c> and field
    /// <c>/AA</c> are host-key restrictions, so the repair removes only those entries and never mutates
    /// the action dictionaries they reference. A merged field/Widget reached by both inventories is one
    /// host and one candidate.
    /// </summary>
    public FormFieldActionRepairPreview PreviewFormFieldActionRepairs()
    {
        FormFieldActionClassification classified = ClassifyFormFieldActionRepairs();
        return new FormFieldActionRepairPreview(
            [.. classified.Candidates.Select(item => item.Candidate)], classified.Refused);
    }

    /// <summary>
    /// Removes the prohibited entries from exactly the selected indirect host objects. A null selection
    /// means every candidate; an empty selection means none. Direct hosts and documents protected by a
    /// signed signature or DocMDP are refused. Referenced action objects are never edited or deleted.
    /// </summary>
    public FormFieldActionRepairReport RepairFormFieldActions(ISet<int>? objectNumbers = null)
    {
        FormFieldActionClassification classified = ClassifyFormFieldActionRepairs();
        var repaired = new List<FormFieldActionRepair>();
        var refused = new List<FormFieldActionRepairRefusal>();

        foreach (FormFieldActionRepairRefusal refusal in classified.Refused)
            if (objectNumbers is null || refusal.ObjectNumber is null || objectNumbers.Contains(refusal.ObjectNumber.Value))
                refused.Add(refusal);

        var available = new HashSet<int>();
        foreach ((FormFieldActionHost host, FormFieldActionRepairCandidate candidate) in classified.Candidates)
        {
            available.Add(candidate.ObjectNumber);
            if (objectNumbers is not null && !objectNumbers.Contains(candidate.ObjectNumber))
                continue;

            bool removedAction = candidate.RemovesAction && host.Dictionary.Remove(FormActionKey);
            bool removedAdditional = candidate.RemovesAdditionalActions
                                     && host.Dictionary.Remove(FormAdditionalActionsKey);
            if (removedAction || removedAdditional)
                repaired.Add(new FormFieldActionRepair(
                    candidate.ObjectNumber, candidate.OwnerKind, removedAction, removedAdditional));
        }

        if (objectNumbers is not null)
            foreach (int requested in objectNumbers)
                if (!available.Contains(requested)
                    && !classified.Refused.Any(refusal => refusal.ObjectNumber == requested))
                    refused.Add(new FormFieldActionRepairRefusal(
                        requested, FormFieldActionOwnerKind.Field,
                        $"Object {requested} is not a current form-field-actions repair candidate."));

        return new FormFieldActionRepairReport(repaired, refused);
    }

    private static readonly PdfName FormActionKey = new("A");
    private static readonly PdfName FormAdditionalActionsKey = new("AA");
    private static readonly PdfName FormSubtypeKey = new("Subtype");
    private static readonly PdfName FormFieldTypeKey = new("FT");
    private static readonly PdfName FormValueKey = new("V");
    private static readonly PdfName FormPermissionsKey = new("Perms");
    private static readonly PdfName FormDocMdpKey = new("DocMDP");

    /// <summary>The single classifier shared by preview and repair.</summary>
    private FormFieldActionClassification ClassifyFormFieldActionRepairs()
    {
        var context = new ConformanceContext(_document, ConformanceProfile.PdfA2b);
        var hosts = new Dictionary<PdfDictionary, FormFieldActionHost>(ReferenceEqualityComparer.Instance);

        FormFieldActionHost Host(PdfDictionary dictionary)
        {
            if (!hosts.TryGetValue(dictionary, out FormFieldActionHost? host))
                hosts[dictionary] = host = new FormFieldActionHost(dictionary);
            return host;
        }

        foreach (PdfDictionary annotation in context.Annotations)
        {
            if (context.ResolveName(annotation.Get(FormSubtypeKey)) != "Widget")
                continue;
            bool hasAction = annotation.ContainsKey(FormActionKey);
            bool hasAdditional = annotation.ContainsKey(FormAdditionalActionsKey);
            if (!hasAction && !hasAdditional)
                continue;

            FormFieldActionHost host = Host(annotation);
            host.IsWidget = true;
            host.RemovesAction |= hasAction;
            host.RemovesAdditionalActions |= hasAdditional;
        }

        foreach (PdfDictionary field in context.FormFields)
        {
            if (!field.ContainsKey(FormAdditionalActionsKey))
                continue;
            FormFieldActionHost host = Host(field);
            host.IsField = true;
            host.RemovesAdditionalActions = true;
        }

        bool protectedDocument = HasDocMdp(context) || HasSignedSignature(context);
        const string protectedReason =
            "Form actions were left in place because this document carries a signed signature or "
          + "DocMDP permission. Pellucid rewrites the file rather than appending a signature-preserving "
          + "revision, so removing the action would invalidate that protection.";

        var candidates = new List<(FormFieldActionHost, FormFieldActionRepairCandidate)>();
        var refused = new List<FormFieldActionRepairRefusal>();
        foreach (FormFieldActionHost host in hosts.Values)
        {
            FormFieldActionOwnerKind kind = host.OwnerKind;
            if (!host.Dictionary.IsIndirect)
            {
                refused.Add(new FormFieldActionRepairRefusal(
                    null, kind,
                    $"A {OwnerLabel(kind)} carrying prohibited form action entries is a direct dictionary, "
                  + "so it has no object number a caller can stage. Make the host indirect or remove the "
                  + "entries by hand."));
                continue;
            }
            if (protectedDocument)
            {
                refused.Add(new FormFieldActionRepairRefusal(host.Dictionary.ObjectNumber, kind, protectedReason));
                continue;
            }

            var candidate = new FormFieldActionRepairCandidate(
                host.Dictionary.ObjectNumber, kind, host.RemovesAction, host.RemovesAdditionalActions);
            candidates.Add((host, candidate));
        }

        return new FormFieldActionClassification(candidates, refused);
    }

    private static string OwnerLabel(FormFieldActionOwnerKind kind) => kind switch
    {
        FormFieldActionOwnerKind.Widget => "Widget annotation",
        FormFieldActionOwnerKind.Field => "form field",
        _ => "merged form field and Widget annotation",
    };

    private static bool HasDocMdp(ConformanceContext context) =>
        context.Resolve(context.Document.CatalogDictionary?.Get(FormPermissionsKey)) is PdfDictionary permissions
        && permissions.ContainsKey(FormDocMdpKey);

    private static bool HasSignedSignature(ConformanceContext context)
    {
        foreach (PdfDictionary field in context.FormFields)
            if (context.ResolveName(field.Get(FormFieldTypeKey)) == "Sig"
                && context.Resolve(field.Get(FormValueKey)) is not null and not PdfNull)
                return true;
        return false;
    }
}
