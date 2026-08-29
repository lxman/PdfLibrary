using PdfLibrary.Conformance;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Editing;

/// <summary>
/// Read-only classification of the PDF/A permissions repair. A candidate is document-scoped because
/// Pellucid performs a full rewrite: repairing any forbidden permissions/signature-reference entry
/// necessarily discards the document's signature and usage-rights proof as one coherent operation.
/// </summary>
public sealed record PermissionsRepairPreview(
    bool IsCandidate,
    int ForbiddenPermissionsKeyCount,
    int ForbiddenDigestKeyCount,
    int SignatureValueCount,
    int SignatureAppearanceCount,
    bool HasDocMdp,
    bool HasUsageRights);

/// <summary>What the permissions repair removed while producing an unsigned derivative.</summary>
public sealed record PermissionsRepairReport(
    bool Repaired,
    int RemovedPermissionsKeyCount,
    int RemovedDigestKeyCount,
    int ClearedSignatureValueCount,
    int ClearedSignatureAppearanceCount,
    int ScrubbedSignatureDictionaryCount,
    bool RemovedDocMdp,
    bool RemovedUsageRights);

public sealed partial class PdfDocumentEditor
{
    private static readonly PdfName PermissionsCatalogKey = new("Perms");
    private static readonly PdfName PermissionsDocMdpKey = new("DocMDP");
    private static readonly PdfName PermissionsUr3Key = new("UR3");
    private static readonly PdfName PermissionsTypeKey = new("Type");
    private static readonly PdfName PermissionsSigType = new("Sig");
    private static readonly PdfName PermissionsSigRefType = new("SigRef");
    private static readonly PdfName PermissionsReferenceKey = new("Reference");
    private static readonly PdfName PermissionsFieldTypeKey = new("FT");
    private static readonly PdfName PermissionsFieldValueKey = new("V");
    private static readonly PdfName PermissionsParentKey = new("Parent");
    private static readonly PdfName PermissionsAppearanceKey = new("AP");
    private static readonly PdfName PermissionsAppearanceStateKey = new("AS");

    private static readonly PdfName[] ProhibitedDigestKeys =
    [
        new("DigestLocation"), new("DigestMethod"), new("DigestValue")
    ];

    // A full rewrite cannot preserve these byte-addressed proof values. Scrubbing the signature
    // dictionary as well as disconnecting it from its field prevents an orphaned object from still
    // advertising stale cryptographic evidence in the saved bytes.
    private static readonly PdfName[] SignatureProofKeys =
    [
        new("Type"), new("Filter"), new("SubFilter"), new("Contents"), new("Cert"),
        new("ByteRange"), new("Reference"), new("Changes"), new("Name"), new("M"),
        new("Location"), new("Reason"), new("ContactInfo"), new("Prop_Build"), new("R")
    ];

    private sealed record PermissionsClassification(
        PdfDictionary Catalog,
        PdfDictionary Permissions,
        IReadOnlyList<PdfName> ForbiddenPermissionsKeys,
        IReadOnlyList<(PdfDictionary Dictionary, PdfName Key)> ForbiddenDigestEntries,
        IReadOnlySet<PdfDictionary> SignatureFieldsAndWidgets,
        IReadOnlySet<PdfDictionary> SignatureDictionaries,
        int SignatureValueCount,
        int SignatureAppearanceCount,
        bool HasDocMdp,
        bool HasUsageRights)
    {
        public bool IsCandidate => ForbiddenPermissionsKeys.Count > 0 || ForbiddenDigestEntries.Count > 0;
    }

    /// <summary>
    /// Classifies clause 6.1.12 violations without mutating the document. The result deliberately
    /// includes every signature value and appearance that the full rewrite would invalidate, so a
    /// caller can obtain explicit consent for an unsigned derivative before staging the repair.
    /// </summary>
    public PermissionsRepairPreview PreviewPermissionsRepair()
    {
        PermissionsClassification? classified = ClassifyPermissionsRepair();
        if (classified is null)
            return new PermissionsRepairPreview(false, 0, 0, 0, 0, false, false);

        return new PermissionsRepairPreview(
            classified.IsCandidate,
            classified.ForbiddenPermissionsKeys.Count,
            classified.ForbiddenDigestEntries.Count,
            classified.SignatureValueCount,
            classified.SignatureAppearanceCount,
            classified.HasDocMdp,
            classified.HasUsageRights);
    }

    /// <summary>
    /// Repairs a current clause 6.1.12 candidate by creating an unsigned derivative. The operation
    /// removes the catalog <c>/Perms</c> entry as a whole, clears signature field values and Widget
    /// appearances, scrubs byte-addressed signature proof dictionaries, and removes every prohibited
    /// legacy digest entry. If the live document is no longer a candidate, nothing is changed.
    /// </summary>
    public PermissionsRepairReport RepairPermissions()
    {
        PermissionsClassification? classified = ClassifyPermissionsRepair();
        if (classified is null || !classified.IsCandidate)
            return new PermissionsRepairReport(false, 0, 0, 0, 0, 0, false, false);

        int removedDigest = 0;
        foreach ((PdfDictionary dictionary, PdfName key) in classified.ForbiddenDigestEntries)
            if (dictionary.Remove(key)) removedDigest++;

        int clearedValues = 0;
        int clearedAppearances = 0;
        foreach (PdfDictionary fieldOrWidget in classified.SignatureFieldsAndWidgets)
        {
            if (fieldOrWidget.Remove(PermissionsFieldValueKey)) clearedValues++;
            if (fieldOrWidget.Remove(PermissionsAppearanceKey)) clearedAppearances++;
            fieldOrWidget.Remove(PermissionsAppearanceStateKey);
        }

        int scrubbedSignatures = 0;
        foreach (PdfDictionary signature in classified.SignatureDictionaries)
        {
            bool changed = false;
            foreach (PdfName key in SignatureProofKeys)
                changed |= signature.Remove(key);
            if (changed) scrubbedSignatures++;
        }

        // Remove the entry from the catalog rather than merely editing its current dictionary. UR3
        // and DocMDP are allowed by PDF/A, but their proof is no longer valid after this full rewrite.
        // Leaving either connected would misrepresent the derivative as still certified/rights-enabled.
        bool removedPermissions = classified.Catalog.Remove(PermissionsCatalogKey);

        return new PermissionsRepairReport(
            removedPermissions || removedDigest > 0 || clearedValues > 0 || clearedAppearances > 0
                               || scrubbedSignatures > 0,
            removedPermissions ? classified.Permissions.Count : 0,
            removedDigest,
            clearedValues,
            clearedAppearances,
            scrubbedSignatures,
            classified.HasDocMdp && removedPermissions,
            classified.HasUsageRights && removedPermissions);
    }

    private PermissionsClassification? ClassifyPermissionsRepair()
    {
        _document.MaterializeAllObjects();
        var context = new ConformanceContext(_document, ConformanceProfile.PdfA2b);
        if (_document.CatalogDictionary is not { } catalog
            || context.Resolve(catalog.Get(PermissionsCatalogKey)) is not PdfDictionary permissions)
            return null;

        List<PdfName> forbiddenPermissions =
        [
            .. permissions.Keys.Where(key => key.Value is not ("UR3" or "DocMDP"))
        ];
        bool hasDocMdp = permissions.ContainsKey(PermissionsDocMdpKey);
        bool hasUsageRights = permissions.ContainsKey(PermissionsUr3Key);

        HashSet<PdfDictionary> allDictionaries = CollectPermissionsDictionaries(context);
        var signatureFieldsAndWidgets = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        foreach (PdfDictionary dictionary in allDictionaries)
            if (IsSignatureFieldOrWidget(context, dictionary))
                signatureFieldsAndWidgets.Add(dictionary);

        var signatureDictionaries = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        foreach (PdfDictionary dictionary in allDictionaries)
            if (context.ResolveName(dictionary.Get(PermissionsTypeKey)) == PermissionsSigType.Value)
                signatureDictionaries.Add(dictionary);
        foreach (PdfDictionary field in signatureFieldsAndWidgets)
            if (context.Resolve(field.Get(PermissionsFieldValueKey)) is PdfDictionary signature)
                signatureDictionaries.Add(signature);
        foreach (PdfName key in new[] { PermissionsDocMdpKey, PermissionsUr3Key })
            if (context.Resolve(permissions.Get(key)) is PdfDictionary signature)
                signatureDictionaries.Add(signature);

        HashSet<PdfDictionary> signatureReferences = CollectSignatureReferences(context, allDictionaries);
        var forbiddenDigest = new List<(PdfDictionary, PdfName)>();
        if (hasDocMdp)
            foreach (PdfDictionary signatureReference in signatureReferences)
            foreach (PdfName key in ProhibitedDigestKeys)
                if (signatureReference.ContainsKey(key))
                    forbiddenDigest.Add((signatureReference, key));

        int signatureValues = signatureFieldsAndWidgets.Count(
            dictionary => context.Resolve(dictionary.Get(PermissionsFieldValueKey)) is not null and not PdfNull);
        int signatureAppearances = signatureFieldsAndWidgets.Count(
            dictionary => context.Resolve(dictionary.Get(PermissionsAppearanceKey)) is not null and not PdfNull);

        return new PermissionsClassification(
            catalog, permissions, forbiddenPermissions, forbiddenDigest,
            signatureFieldsAndWidgets, signatureDictionaries,
            signatureValues, signatureAppearances, hasDocMdp, hasUsageRights);
    }

    private static HashSet<PdfDictionary> CollectPermissionsDictionaries(ConformanceContext context)
    {
        var dictionaries = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        foreach (PdfObject value in context.Document.Objects.Values)
        {
            if (value is PdfDictionary dictionary) dictionaries.Add(dictionary);
            else if (value is PdfStream stream) dictionaries.Add(stream.Dictionary);
        }
        foreach (PdfDictionary field in context.FormFields) dictionaries.Add(field);
        foreach (PdfDictionary annotation in context.Annotations) dictionaries.Add(annotation);
        return dictionaries;
    }

    private static HashSet<PdfDictionary> CollectSignatureReferences(
        ConformanceContext context, IEnumerable<PdfDictionary> dictionaries)
    {
        var references = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        foreach (PdfDictionary dictionary in dictionaries)
        {
            if (context.ResolveName(dictionary.Get(PermissionsTypeKey)) == PermissionsSigRefType.Value)
                references.Add(dictionary);
            if (context.Resolve(dictionary.Get(PermissionsReferenceKey)) is not PdfArray array) continue;
            foreach (PdfObject entry in array)
                if (context.Resolve(entry) is PdfDictionary signatureReference)
                    references.Add(signatureReference);
        }
        return references;
    }

    private static bool IsSignatureFieldOrWidget(ConformanceContext context, PdfDictionary dictionary)
    {
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        PdfDictionary? current = dictionary;
        while (current is not null && seen.Add(current))
        {
            if (context.ResolveName(current.Get(PermissionsFieldTypeKey)) == PermissionsSigType.Value)
                return true;
            current = context.Resolve(current.Get(PermissionsParentKey)) as PdfDictionary;
        }
        return false;
    }
}
