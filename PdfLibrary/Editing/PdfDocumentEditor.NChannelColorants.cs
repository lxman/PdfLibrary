using PdfLibrary.Conformance;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Optimization;

namespace PdfLibrary.Editing;

/// <summary>One missing DeviceN spot-colorant fallback that can reuse an existing Separation object.</summary>
public sealed record NChannelColorantRepairCandidate(
    int? DeviceNObjectNumber,
    int? AttributesObjectNumber,
    string Colorant,
    int SeparationObjectNumber,
    bool CreatesAttributesDictionary,
    bool CreatesColorantsDictionary);

/// <summary>One missing DeviceN spot-colorant fallback deliberately left unchanged.</summary>
public sealed record NChannelColorantRepairRefusal(
    int? DeviceNObjectNumber,
    int? AttributesObjectNumber,
    string Colorant,
    string Reason);

/// <summary>Read-only classification of all missing DeviceN spot-colorant fallback definitions.</summary>
public sealed record NChannelColorantsRepairPreview(
    IReadOnlyList<NChannelColorantRepairCandidate> Candidates,
    IReadOnlyList<NChannelColorantRepairRefusal> Refused);

/// <summary>One DeviceN <c>/Colorants</c> entry linked to an existing indirect Separation object.</summary>
public sealed record NChannelColorantRepair(
    int? DeviceNObjectNumber,
    int? AttributesObjectNumber,
    string Colorant,
    int SeparationObjectNumber,
    bool CreatedAttributesDictionary,
    bool CreatedColorantsDictionary);

/// <summary>What the DeviceN colorants repair changed or refused.</summary>
public sealed record NChannelColorantsRepairReport(
    IReadOnlyList<NChannelColorantRepair> Repaired,
    IReadOnlyList<NChannelColorantRepairRefusal> Refused);

public sealed partial class PdfDocumentEditor
{
    private static readonly PdfName NChannelColorantsKey = new("Colorants");

    private sealed record NChannelColorantAction(
        PdfArray DeviceN,
        PdfDictionary? Attributes,
        PdfDictionary? Colorants,
        PdfArray Separation,
        NChannelColorantRepairCandidate Candidate);

    private sealed record NChannelColorantsClassification(
        IReadOnlyList<NChannelColorantAction> Candidates,
        IReadOnlyList<NChannelColorantRepairRefusal> Refused);

    /// <summary>
    /// Classifies missing PDF/A DeviceN spot-colorant fallback definitions without writing. A candidate
    /// exists only when the exact fallback already exists as one unambiguous indirect Separation array.
    /// The repair links that object; it never derives an individual tint transform from the DeviceN's
    /// combined transform, clones a direct value, replaces malformed data, or chooses among definitions.
    /// </summary>
    public NChannelColorantsRepairPreview PreviewNChannelColorantsRepair(bool nChannelOnly = false)
    {
        NChannelColorantsClassification classified = ClassifyNChannelColorantsRepair(nChannelOnly);
        return new NChannelColorantsRepairPreview(
            [.. classified.Candidates.Select(item => item.Candidate)], classified.Refused);
    }

    /// <summary>
    /// Adds each provable missing <c>/Colorants</c> entry as an indirect reference to its already-present
    /// Separation definition. Missing container dictionaries may be created, but existing values are never
    /// replaced. Signed and DocMDP-protected documents are left unchanged.
    /// </summary>
    public NChannelColorantsRepairReport RepairNChannelColorants(bool nChannelOnly = false)
    {
        NChannelColorantsClassification classified = ClassifyNChannelColorantsRepair(nChannelOnly);
        var repaired = new List<NChannelColorantRepair>();
        var createdAttributes = new Dictionary<PdfArray, PdfDictionary>(ReferenceEqualityComparer.Instance);
        var createdColorants = new Dictionary<PdfDictionary, PdfDictionary>(ReferenceEqualityComparer.Instance);

        foreach (NChannelColorantAction action in classified.Candidates)
        {
            PdfDictionary attributes;
            bool madeAttributes = false;
            if (action.Attributes is not null)
            {
                attributes = action.Attributes;
            }
            else if (!createdAttributes.TryGetValue(action.DeviceN, out attributes!))
            {
                attributes = new PdfDictionary();
                action.DeviceN.Add(attributes);
                createdAttributes[action.DeviceN] = attributes;
                madeAttributes = true;
            }

            PdfDictionary colorants;
            bool madeColorants = false;
            if (action.Colorants is not null)
            {
                colorants = action.Colorants;
            }
            else if (!createdColorants.TryGetValue(attributes, out colorants!))
            {
                colorants = new PdfDictionary();
                attributes[NChannelColorantsKey] = colorants;
                createdColorants[attributes] = colorants;
                madeColorants = true;
            }

            var colorantKey = new PdfName(action.Candidate.Colorant);
            if (colorants.ContainsKey(colorantKey))
                continue;

            colorants[colorantKey] = new PdfIndirectReference(
                action.Separation.ObjectNumber, action.Separation.GenerationNumber);
            repaired.Add(new NChannelColorantRepair(
                action.Candidate.DeviceNObjectNumber,
                action.Candidate.AttributesObjectNumber,
                action.Candidate.Colorant,
                action.Candidate.SeparationObjectNumber,
                madeAttributes,
                madeColorants));
        }

        return new NChannelColorantsRepairReport(repaired, classified.Refused);
    }

    private NChannelColorantsClassification ClassifyNChannelColorantsRepair(bool nChannelOnly)
    {
        var context = new ConformanceContext(_document, ConformanceProfile.PdfA2b);
        SpotColourInventory.Collect(context, out List<SeparationDef> separations, out List<DeviceNDef> deviceNs);
        HashSet<int> reachable = ObjectGraphWalker.CollectReachable(_document);
        HashSet<PdfObject> reachableObjects = CollectNChannelReachableObjects(reachable);
        var candidates = new List<NChannelColorantAction>();
        var refused = new List<NChannelColorantRepairRefusal>();

        foreach (DeviceNDef deviceN in deviceNs)
        {
            PdfDictionary? attributes = deviceN.Attributes;
            if (nChannelOnly && context.ResolveName(attributes?.Get("Subtype")) != "NChannel")
                continue;
            PdfDictionary? colorants = null;
            string? malformedContainerReason = null;

            if (attributes is null && deviceN.Source.Count >= 5)
            {
                malformedContainerReason =
                    "The DeviceN attributes value is not a dictionary. Replacing it would discard existing "
                  + "document data, so Pellucid left it unchanged.";
            }
            else if (attributes is not null && attributes.ContainsKey(NChannelColorantsKey))
            {
                colorants = context.Resolve(attributes.Get(NChannelColorantsKey)) as PdfDictionary;
                if (colorants is null)
                    malformedContainerReason =
                        "The /Colorants value is not a dictionary. Replacing it would discard existing "
                      + "document data, so Pellucid left it unchanged.";
            }

            foreach (string colorant in deviceN.Colorants.Distinct(StringComparer.Ordinal))
            {
                if (colorant is "None" or "All" || SpotColourInventory.ProcessColorants.Contains(colorant))
                    continue;
                if (colorants?.Get(new PdfName(colorant)) is not null)
                    continue;

                bool sourceReachable = deviceN.Source.IsIndirect
                    ? reachable.Contains(deviceN.Source.ObjectNumber)
                    : reachableObjects.Contains(deviceN.Source);
                if (!sourceReachable)
                {
                    AddRefusal(deviceN, colorant,
                        "The DeviceN colour space is unreachable. Pellucid will not modify an orphan that an "
                      + "ordinary full rewrite removes, so it left the colour space unchanged.");
                    continue;
                }

                if (malformedContainerReason is not null)
                {
                    AddRefusal(deviceN, colorant, malformedContainerReason);
                    continue;
                }

                List<PdfArray> matches = [.. separations
                    .Where(def => def.Colorant == colorant
                                  && def.Source.IsIndirect
                                  && reachable.Contains(def.Source.ObjectNumber))
                    .Select(def => def.Source)
                    .DistinctBy(array => (array.ObjectNumber, array.GenerationNumber))];
                bool hasUnlinkableMatch = separations.Any(def =>
                    def.Colorant == colorant
                    && (!def.Source.IsIndirect || !reachable.Contains(def.Source.ObjectNumber)));

                if (matches.Count == 0)
                {
                    string reason = hasUnlinkableMatch
                        ? $"The only Separation definition for spot colorant '{colorant}' is direct or unreachable. "
                          + "Pellucid will not clone it or resurrect an orphan because that would invent or activate "
                          + "a fallback object identity."
                        : $"No existing Separation definition describes spot colorant '{colorant}'. The DeviceN "
                          + "tint transform describes colorants in combination and cannot prove an individual fallback.";
                    AddRefusal(deviceN, colorant, reason);
                    continue;
                }

                if (matches.Count > 1)
                {
                    AddRefusal(deviceN, colorant,
                        $"More than one indirect Separation definition describes spot colorant '{colorant}'. "
                      + "Pellucid will not choose an external plate or fallback identity on the document's behalf.");
                    continue;
                }

                PdfObject target = (PdfObject?)attributes ?? deviceN.Source;
                if (candidates.Any(item =>
                        ReferenceEquals((PdfObject?)item.Attributes ?? item.DeviceN, target)
                        && item.Candidate.Colorant == colorant))
                    continue;

                PdfArray separation = matches[0];
                candidates.Add(new NChannelColorantAction(
                    deviceN.Source,
                    attributes,
                    colorants,
                    separation,
                    new NChannelColorantRepairCandidate(
                        deviceN.Source.IsIndirect ? deviceN.Source.ObjectNumber : null,
                        attributes is { IsIndirect: true } ? attributes.ObjectNumber : null,
                        colorant,
                        separation.ObjectNumber,
                        attributes is null,
                        colorants is null)));
            }
        }

        if (candidates.Count == 0 || (!HasDocMdp(context) && !HasSignedSignature(context)))
            return new NChannelColorantsClassification(candidates, refused);

        const string protectedReason =
            "The missing spot-colorant fallback was left unchanged because this document carries a signed "
          + "signature or DocMDP permission. Pellucid performs a full rewrite rather than a signature-preserving "
          + "append, so adding the fallback would invalidate that protection.";
        refused.AddRange(candidates.Select(item => new NChannelColorantRepairRefusal(
            item.Candidate.DeviceNObjectNumber,
            item.Candidate.AttributesObjectNumber,
            item.Candidate.Colorant,
            protectedReason)));
        return new NChannelColorantsClassification([], refused);

        HashSet<PdfObject> CollectNChannelReachableObjects(IReadOnlySet<int> objectNumbers)
        {
            var objects = new HashSet<PdfObject>(ReferenceEqualityComparer.Instance);
            foreach (int objectNumber in objectNumbers)
                if (_document.GetObject(objectNumber) is { } value)
                    Visit(value);
            return objects;

            void Visit(PdfObject value)
            {
                if (!objects.Add(value))
                    return;
                switch (value)
                {
                    case PdfStream stream:
                        Visit(stream.Dictionary);
                        break;
                    case PdfDictionary dictionary:
                        foreach (PdfObject child in dictionary.Values)
                            if (child is not PdfIndirectReference)
                                Visit(child);
                        break;
                    case PdfArray array:
                        foreach (PdfObject child in array)
                            if (child is not PdfIndirectReference)
                                Visit(child);
                        break;
                }
            }
        }

        void AddRefusal(DeviceNDef deviceN, string colorant, string reason)
        {
            int? attributesObjectNumber = deviceN.Attributes is { IsIndirect: true }
                ? deviceN.Attributes.ObjectNumber
                : null;
            if (refused.Any(item =>
                    item.DeviceNObjectNumber == (deviceN.Source.IsIndirect ? deviceN.Source.ObjectNumber : null)
                    && item.AttributesObjectNumber == attributesObjectNumber
                    && item.Colorant == colorant
                    && item.Reason == reason))
                return;
            refused.Add(new NChannelColorantRepairRefusal(
                deviceN.Source.IsIndirect ? deviceN.Source.ObjectNumber : null,
                attributesObjectNumber,
                colorant,
                reason));
        }
    }
}
