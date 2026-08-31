using PdfLibrary.Core.Primitives;
using PdfLibrary.Core;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A-2/3 clause 6.4.3 test 1 requires a digital signature's <c>/ByteRange</c> to cover the
/// complete file except for the signature value itself. This rule owns the narrow, byte-provable
/// save-regression part of that contract: a structurally plausible four-integer range that starts at
/// byte zero must not leave bytes after its final covered range.
///
/// <para>The raw source is deliberately required. A parsed object graph cannot reveal bytes appended
/// after the signed revision, which is the production failure this rule was added for. In-memory-only
/// preflight therefore skips rather than guessing. Interior gaps are checked for a well-formed,
/// ordered, non-overlapping range array, but this rule does not parse PKCS#7 or prove that the one
/// excluded gap is exactly the serialized <c>/Contents</c> token; those are separate cryptographic
/// concerns. Malformed or physically impossible arrays are therefore left to a validator with a
/// signature-aware source parser instead of being guessed from the object graph.</para>
/// </summary>
internal sealed class SignatureByteRangeRule : IConformanceRule
{
    public string RuleId => "signature-byte-range";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        byte[]? source = context.SourceBytes;
        if (source is null)
            yield break;

        foreach (PdfDictionary signature in Signatures(context))
        {
            if (!HasTrailingUnsignedBytes(context, signature, source.LongLength))
                continue;

            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "6.4.3"),
                Message = "The digital signature's /ByteRange leaves bytes after the signed revision "
                          + "and does not reach the physical end of the document.",
                ObjectNumber = signature.IsIndirect ? signature.ObjectNumber : null,
            };
        }
    }

    private static IReadOnlyCollection<PdfDictionary> Signatures(ConformanceContext context)
    {
        var signatures = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        // A signature dictionary is reached through a signature field's /V. Include that path even
        // when a malformed producer omitted the signature dictionary's optional /Type marker: veraPDF's
        // PDSignature model is field-reachable, not limited to a whole-object /Type census.
        foreach (PdfDictionary field in context.FormFields)
            if (EffectiveFieldType(context, field) == "Sig"
                && context.Resolve(field.Get("V")) is PdfDictionary signature)
                signatures.Add(signature);

        // Certification and usage-rights signatures can also be rooted in the catalogue permission
        // dictionary. Do not census every /Type /Sig object: unreachable signature-shaped objects are
        // not active signatures and veraPDF does not model them as PDSignature instances.
        if (context.Resolve(context.Catalog?.Dictionary.Get("Perms")) is PdfDictionary permissions)
            foreach (PdfObject value in permissions.Values)
                if (context.Resolve(value) is PdfDictionary signature
                    && context.ResolveName(signature.Get("Type")) == "Sig")
                    signatures.Add(signature);

        return signatures;
    }

    private static string? EffectiveFieldType(ConformanceContext context, PdfDictionary field)
    {
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        PdfDictionary? current = field;
        while (current is not null && seen.Add(current))
        {
            if (context.ResolveName(current.Get("FT")) is { } type)
                return type;
            current = context.Resolve(current.Get("Parent")) as PdfDictionary;
        }
        return null;
    }

    private static bool HasTrailingUnsignedBytes(
        ConformanceContext context, PdfDictionary signature, long fileLength)
    {
        if (context.Resolve(signature.Get("ByteRange")) is not PdfArray range
            || range.Count != 4)
            return false;

        if (context.Resolve(range[0]) is not PdfInteger firstOffsetValue
            || context.Resolve(range[1]) is not PdfInteger firstLengthValue
            || context.Resolve(range[2]) is not PdfInteger secondOffsetValue
            || context.Resolve(range[3]) is not PdfInteger secondLengthValue)
            return false;

        long firstOffset = firstOffsetValue.Value;
        long firstLength = firstLengthValue.Value;
        long secondOffset = secondOffsetValue.Value;
        long secondLength = secondLengthValue.Value;
        if (firstOffset != 0 || firstLength < 0 || secondOffset < firstLength
            || secondOffset < 0 || secondLength < 0 || secondOffset > fileLength
            || secondLength > fileLength - secondOffset)
            return false;

        return secondOffset + secondLength < fileLength;
    }
}
