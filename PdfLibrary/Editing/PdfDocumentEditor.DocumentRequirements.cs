using PdfLibrary.Conformance;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Editing;

/// <summary>The catalog host entry that declares processor requirements for the document.</summary>
public sealed record DocumentRequirementsRepairCandidate(
    int? CatalogObjectNumber,
    int? RequirementsObjectNumber);

/// <summary>A catalog <c>/Requirements</c> entry deliberately left in place.</summary>
public sealed record DocumentRequirementsRepairRefusal(
    int? CatalogObjectNumber,
    int? RequirementsObjectNumber,
    string Reason);

/// <summary>Read-only classification of the catalog <c>/Requirements</c> entry.</summary>
public sealed record DocumentRequirementsRepairPreview(
    DocumentRequirementsRepairCandidate? Candidate,
    DocumentRequirementsRepairRefusal? Refused);

/// <summary>The catalog <c>/Requirements</c> host key removed by the repair.</summary>
public sealed record DocumentRequirementsRepair(
    int? CatalogObjectNumber,
    int? RequirementsObjectNumber);

/// <summary>What the document-requirements repair changed or refused.</summary>
public sealed record DocumentRequirementsRepairReport(
    DocumentRequirementsRepair? Repaired,
    DocumentRequirementsRepairRefusal? Refused);

public sealed partial class PdfDocumentEditor
{
    private static readonly PdfName DocumentRequirementsKey = new("Requirements");

    /// <summary>
    /// Classifies the catalog <c>/Requirements</c> host without writing. The host key is the PDF/A
    /// prohibited construct. Detaching it does not mutate a direct or indirect requirement array,
    /// requirement dictionary, handler dictionary, or named script that another host may share.
    /// Malformed values remain removable because the conformance rule is presence-only.
    /// </summary>
    public DocumentRequirementsRepairPreview PreviewDocumentRequirementsRepair()
    {
        PdfDictionary? catalog = _document.CatalogDictionary;
        if (catalog is null || !catalog.ContainsKey(DocumentRequirementsKey))
            return new DocumentRequirementsRepairPreview(null, null);

        PdfObject? requirements = ResolveObject(catalog.Get(DocumentRequirementsKey));
        int? requirementsObjectNumber = requirements is { IsIndirect: true }
            ? requirements.ObjectNumber
            : catalog.Get(DocumentRequirementsKey) is PdfIndirectReference reference
                ? reference.ObjectNumber
                : null;
        int? catalogObjectNumber = catalog.IsIndirect ? catalog.ObjectNumber : null;

        var context = new ConformanceContext(_document, ConformanceProfile.PdfA2b);
        if (!HasDocMdp(context) && !HasSignedSignature(context))
            return new DocumentRequirementsRepairPreview(
                new DocumentRequirementsRepairCandidate(catalogObjectNumber, requirementsObjectNumber), null);

        const string reason =
            "The document requirement declaration was left in place because this document carries a "
          + "signed signature or DocMDP permission. Pellucid rewrites the file rather than appending a "
          + "signature-preserving revision, so removing the processor requirement would invalidate that protection.";
        return new DocumentRequirementsRepairPreview(
            null,
            new DocumentRequirementsRepairRefusal(catalogObjectNumber, requirementsObjectNumber, reason));
    }

    /// <summary>
    /// Removes only the catalog <c>/Requirements</c> host key. This is an explicit feature-loss operation:
    /// processors will no longer be told to require a declared feature or to coordinate alternative
    /// requirement handlers. The referenced objects themselves are never edited.
    /// </summary>
    public DocumentRequirementsRepairReport RepairDocumentRequirements()
    {
        DocumentRequirementsRepairPreview preview = PreviewDocumentRequirementsRepair();
        if (preview.Candidate is null)
            return new DocumentRequirementsRepairReport(null, preview.Refused);

        PdfDictionary? catalog = _document.CatalogDictionary;
        if (catalog is null || !catalog.Remove(DocumentRequirementsKey))
            return new DocumentRequirementsRepairReport(null, null);

        return new DocumentRequirementsRepairReport(
            new DocumentRequirementsRepair(
                preview.Candidate.CatalogObjectNumber,
                preview.Candidate.RequirementsObjectNumber),
            null);
    }
}
