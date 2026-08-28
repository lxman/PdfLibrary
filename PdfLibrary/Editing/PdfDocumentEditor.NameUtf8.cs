using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Editing;

/// <summary>
/// One invalid PDF name whose role and complete in-document use are narrow enough to normalize safely.
/// The replacement is an ASCII fallback: existing ASCII bytes are preserved and every non-ASCII byte
/// becomes an auditable <c>~HH</c> token. No legacy character encoding is guessed.
/// </summary>
public sealed record NameUtf8RepairCandidate(
    int ObjectNumber,
    int ArrayIndex,
    string OriginalBytesHex,
    string ReplacementName,
    int ConsumerCount);

/// <summary>An invalid name condition the editor deliberately leaves unchanged.</summary>
public sealed record NameUtf8RepairRefusal(string Reason);

/// <summary>Read-only classification of the current document's invalid UTF-8 names.</summary>
public sealed record NameUtf8RepairPreview(
    NameUtf8RepairCandidate? Candidate,
    IReadOnlyList<NameUtf8RepairRefusal> Refused);

/// <summary>The exact name value changed by one repair.</summary>
public sealed record NameUtf8Repair(
    int ObjectNumber,
    int ArrayIndex,
    string OriginalBytesHex,
    string ReplacementName,
    int ConsumerCount);

/// <summary>What current-document reclassification changed or refused.</summary>
public sealed record NameUtf8RepairReport(
    NameUtf8Repair? Repaired,
    IReadOnlyList<NameUtf8RepairRefusal> Refused);

public sealed partial class PdfDocumentEditor
{
    private sealed record InvalidNameOccurrence(
        int OwnerObjectNumber,
        PdfArray? Array,
        int ArrayIndex,
        bool IsDictionaryKey,
        string Value);

    private sealed record NameUtf8Classification(
        PdfArray? Array,
        NameUtf8RepairCandidate? Candidate,
        IReadOnlyList<NameUtf8RepairRefusal> Refused);

    private static readonly UTF8Encoding NameUtf8Strict =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly PdfName NameUtf8Separation = new("Separation");
    private static readonly PdfName NameUtf8Resources = new("Resources");
    private static readonly PdfName NameUtf8ColorSpace = new("ColorSpace");
    private static readonly PdfName NameUtf8Perms = new("Perms");
    private static readonly PdfName NameUtf8DocMdp = new("DocMDP");

    /// <summary>
    /// Classifies invalid UTF-8 names without mutation. The supported shape is intentionally narrow:
    /// exactly one invalid name occurrence, at index 1 of an indirect four-element <c>/Separation</c>
    /// colour-space array, referenced only by page-resource <c>/ColorSpace</c> dictionaries. Dictionary
    /// keys, resource identifiers, repeated names, signatures, collisions, and every other role refuse.
    /// </summary>
    public NameUtf8RepairPreview PreviewNameUtf8Repair()
    {
        NameUtf8Classification classification = ClassifyNameUtf8Repair();
        return new NameUtf8RepairPreview(classification.Candidate, classification.Refused);
    }

    /// <summary>
    /// Reclassifies the live object graph and replaces only the proven <c>/Separation</c> colourant-name
    /// value. The alternate colour space, tint transform, resource keys, and every reference are retained.
    /// </summary>
    public NameUtf8RepairReport RepairNameUtf8()
    {
        NameUtf8Classification classification = ClassifyNameUtf8Repair();
        if (classification.Array is null || classification.Candidate is null)
            return new NameUtf8RepairReport(null, classification.Refused);

        NameUtf8RepairCandidate candidate = classification.Candidate;
        classification.Array[candidate.ArrayIndex] = new PdfName(candidate.ReplacementName);
        return new NameUtf8RepairReport(
            new NameUtf8Repair(
                candidate.ObjectNumber,
                candidate.ArrayIndex,
                candidate.OriginalBytesHex,
                candidate.ReplacementName,
                candidate.ConsumerCount),
            classification.Refused);
    }

    private NameUtf8Classification ClassifyNameUtf8Repair()
    {
        _document.MaterializeAllObjects();
        var invalid = new List<InvalidNameOccurrence>();
        var allNames = new HashSet<string>(StringComparer.Ordinal);
        foreach ((int owner, PdfObject obj) in _document.Objects.OrderBy(pair => pair.Key))
            CollectNameOccurrences(obj, owner, invalid, allNames);

        if (invalid.Count == 0)
            return new NameUtf8Classification(null, null, []);

        if (invalid.Count != 1)
            return Refuse(
                $"The document contains {invalid.Count} invalid UTF-8 name occurrences. Pellucid only "
              + "normalizes one uniquely located name at a time and will not guess which repeated or "
              + "independent identifiers are intended to match.");

        InvalidNameOccurrence occurrence = invalid[0];
        if (occurrence.IsDictionaryKey)
            return Refuse(
                "The invalid UTF-8 name is a dictionary key. Renaming a key can change object semantics "
              + "and may require coordinated reference updates, so Pellucid leaves it unchanged.");

        if (occurrence.Array is not { IsIndirect: true } array
            || occurrence.ArrayIndex != 1
            || array.Count != 4
            || array[0] is not PdfName { Value: "Separation" })
            return Refuse(
                "The invalid UTF-8 name is not the colourant-name value at index 1 of an indirect, "
              + "four-element /Separation colour-space array. This program does not rename other name roles.");

        if (array.ObjectNumber != occurrence.OwnerObjectNumber)
            return Refuse(
                "The invalid /Separation colourant name is nested inside another indirect object rather "
              + "than owned by the independently addressable colour-space array.");

        var context = new ConformanceContext(_document, ConformanceProfile.PdfA2b);
        if (HasNameUtf8SignatureProtection(context))
            return Refuse(
                "The invalid colourant name was left unchanged because the document carries a signed "
              + "signature or DocMDP permission. Pellucid performs a full rewrite and does not claim to "
              + "preserve that protection.");

        int allReferences = CountReferencesTo(array.ObjectNumber);
        int pageColourSpaceReferences = CountDirectPageColourSpaceReferences(context, array.ObjectNumber);
        if (allReferences == 0 || allReferences != pageColourSpaceReferences)
            return Refuse(
                $"The invalid /Separation colour space has {allReferences} indirect reference(s), but "
              + $"only {pageColourSpaceReferences} are direct page-resource /ColorSpace entries. "
              + "Pellucid will not rename a colourant used through an unproven consumer path.");

        string replacement = BuildAuditableAsciiName(occurrence.Value);
        if (!IsNameUtf8Valid(replacement))
            return Refuse("The deterministic replacement unexpectedly is not valid UTF-8.");
        if (allNames.Contains(replacement))
            return Refuse(
                $"The deterministic replacement /{replacement} already exists in the document. "
              + "Pellucid will not merge two distinct PDF names.");

        var candidate = new NameUtf8RepairCandidate(
            array.ObjectNumber,
            occurrence.ArrayIndex,
            Convert.ToHexString(Encoding.Latin1.GetBytes(occurrence.Value)),
            replacement,
            allReferences);
        return new NameUtf8Classification(array, candidate, []);
    }

    private NameUtf8Classification Refuse(string reason) =>
        new(null, null, [new NameUtf8RepairRefusal(reason)]);

    private static void CollectNameOccurrences(
        PdfObject? obj,
        int owner,
        List<InvalidNameOccurrence> invalid,
        HashSet<string> allNames,
        PdfArray? containingArray = null,
        int arrayIndex = -1)
    {
        switch (obj)
        {
            case PdfName name:
                allNames.Add(name.Value);
                if (!IsNameUtf8Valid(name.Value))
                    invalid.Add(new InvalidNameOccurrence(
                        owner, containingArray, arrayIndex, IsDictionaryKey: false, name.Value));
                break;

            case PdfDictionary dictionary:
                foreach ((PdfName key, PdfObject value) in dictionary)
                {
                    allNames.Add(key.Value);
                    if (!IsNameUtf8Valid(key.Value))
                        invalid.Add(new InvalidNameOccurrence(
                            owner, null, -1, IsDictionaryKey: true, key.Value));
                    CollectNameOccurrences(value, owner, invalid, allNames);
                }
                break;

            case PdfStream stream:
                CollectNameOccurrences(stream.Dictionary, owner, invalid, allNames);
                break;

            case PdfArray array:
                for (var i = 0; i < array.Count; i++)
                    CollectNameOccurrences(array[i], owner, invalid, allNames, array, i);
                break;
        }
    }

    private int CountReferencesTo(int objectNumber)
    {
        var count = 0;
        foreach (PdfObject obj in _document.Objects.Values)
            count += CountReferencesTo(obj, objectNumber);
        return count;
    }

    private static int CountReferencesTo(PdfObject? obj, int objectNumber)
    {
        return obj switch
        {
            PdfIndirectReference reference => reference.ObjectNumber == objectNumber ? 1 : 0,
            PdfDictionary dictionary => dictionary.Values.Sum(value => CountReferencesTo(value, objectNumber)),
            PdfStream stream => CountReferencesTo(stream.Dictionary, objectNumber),
            PdfArray array => array.Sum(value => CountReferencesTo(value, objectNumber)),
            _ => 0,
        };
    }

    private static int CountDirectPageColourSpaceReferences(ConformanceContext context, int objectNumber)
    {
        var count = 0;
        foreach (var page in context.Pages)
        {
            if (context.Resolve(page.Dictionary.Get(NameUtf8Resources)) is not PdfDictionary resources
                || context.Resolve(resources.Get(NameUtf8ColorSpace)) is not PdfDictionary colourSpaces)
                continue;
            foreach (PdfObject value in colourSpaces.Values)
                if (value is PdfIndirectReference reference && reference.ObjectNumber == objectNumber)
                    count++;
        }
        return count;
    }

    private static bool HasNameUtf8SignatureProtection(ConformanceContext context)
    {
        if (context.Resolve(context.Catalog?.Dictionary.Get(NameUtf8Perms)) is PdfDictionary permissions
            && permissions.ContainsKey(NameUtf8DocMdp))
            return true;

        return context.Document.Objects.Values.Any(obj => ContainsNameUtf8Signature(context, obj));
    }

    private static bool ContainsNameUtf8Signature(ConformanceContext context, PdfObject? obj)
    {
        switch (obj)
        {
            case PdfStream stream:
                return ContainsNameUtf8Signature(context, stream.Dictionary);
            case PdfDictionary dictionary:
                if (context.ResolveName(dictionary.Get("Type")) == "Sig"
                    && (dictionary.Get("ByteRange") is not null || dictionary.Get("Contents") is not null))
                    return true;
                return dictionary.Values.Any(value => ContainsNameUtf8Signature(context, value));
            case PdfArray array:
                return array.Any(value => ContainsNameUtf8Signature(context, value));
            // Every indirect object is visited from Document.Objects. Do not resolve here: following
            // references would revisit graph cycles such as Page -> Parent -> Kids -> Page.
            default:
                return false;
        }
    }

    private static bool IsNameUtf8Valid(string value)
    {
        try
        {
            NameUtf8Strict.GetCharCount(Encoding.Latin1.GetBytes(value));
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static string BuildAuditableAsciiName(string value)
    {
        var result = new StringBuilder();
        foreach (byte valueByte in Encoding.Latin1.GetBytes(value))
        {
            if (valueByte < 0x80)
                result.Append((char)valueByte);
            else
                result.Append('~').Append(valueByte.ToString("X2"));
        }
        return result.ToString();
    }
}
