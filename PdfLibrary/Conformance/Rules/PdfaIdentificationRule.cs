using System.Linq;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Metadata;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A requires the XMP metadata to carry the PDF/A identification schema (pdfaid): a
/// <c>pdfaid:part</c> matching the ISO 19005 part being targeted and a <c>pdfaid:conformance</c>
/// level valid for that target (ISO 19005-2, 6.6.4). Missing or mismatched identification is an error.
/// </summary>
internal sealed class PdfaIdentificationRule : IConformanceRule
{
    // The PDF/A identification namespace URI (ISO 19005-1, Annex). No shared constant exists yet.
    private const string PdfaIdNs = "http://www.aiim.org/pdfa/ns/id/";

    private const string PdfaIdPrefix = "pdfaid";

    /// <summary>The identification properties whose namespace prefix ISO 19005-2 6.6.4 constrains —
    /// tests 4, 5, 6 and 7, one per name. <c>amd</c> and <c>corr</c> are optional and absent from every
    /// corpus fixture, so only the first two are exercised end-to-end; they are handled here anyway
    /// because they are the SAME check on two more names, and leaving them out would let a document
    /// with a mis-prefixed amendment identifier pass us while failing veraPDF.</summary>
    private static readonly string[] PrefixedProperties = ["part", "conformance", "amd", "corr"];

    public string RuleId => "pdfa-id";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        PdfStream? metadata = context.Document.GetCatalog()?.GetMetadata();
        if (metadata is null)
        {
            yield return Error(context.Target,
                "The XMP metadata is missing, so PDF/A identification (pdfaid) cannot be verified.");
            yield break;
        }

        byte[] xmpBytes = metadata.GetDecodedData(context.Document.Decryptor);
        XmpPacket packet = XmpPacket.Parse(xmpBytes);

        // 6.6.4 tests 4-7: every PDF/A-identification property must carry the namespace PREFIX
        // "pdfaid" literally, not merely the right namespace URI. XML normally treats a prefix as an
        // interchangeable alias for its URI, so this reads as a spec quirk — but ISO 19005-2 6.6.4
        // mandates the prefix, veraPDF enforces it, and a document binding the same URI to "pdfa"
        // conforms by every other measure while failing this one (corpus fixture
        // "6-6-4-t01-fail-b.pdf" is exactly that). Checked BEFORE the value tests below, which look
        // properties up by URI and would therefore pass such a file silently.
        //
        // A null/empty prefix is accepted, mirroring veraPDF's own `partPrefix == null || …` shape:
        // a property carrying no prefix is a different (and unmandated) situation, not a wrong one.
        foreach (string property in PrefixedProperties)
        {
            if (packet.Get(PdfaIdNs, property)?.Prefix is not { Length: > 0 } prefix) continue;
            if (prefix == PdfaIdPrefix) continue;

            yield return Error(context.Target,
                $"The PDF/A identification property '{property}' uses the namespace prefix "
                + $"'{prefix}', but ISO 19005 requires '{PdfaIdPrefix}'.");
        }

        string? part = packet.Get(PdfaIdNs, "part")?.Value?.Trim();
        string? conformance = packet.Get(PdfaIdNs, "conformance")?.Value?.Trim();

        if (part is null || conformance is null)
        {
            yield return Error(context.Target,
                "The XMP metadata lacks PDF/A identification (pdfaid:part and/or pdfaid:conformance).");
            yield break;
        }

        // PDF/A-3 is ISO 19005 part 3; PDF/A-2b and -2u are part 2.
        string expectedPart = context.Target == ConformanceProfile.PdfA3b ? "3" : "2";

        // Level B and U profiles accept the corresponding conformance letter (and any stricter one);
        // a "u" (Unicode) profile does not accept the weaker "B".
        string[] acceptedConformance = context.Target == ConformanceProfile.PdfA2u
            ? ["U", "A"]
            : ["B", "U", "A"];

        if (part != expectedPart)
        {
            yield return Error(context.Target,
                $"pdfaid:part is '{part}', but the target profile requires part {expectedPart}.");
        }

        if (!acceptedConformance.Contains(conformance))
        {
            yield return Error(context.Target,
                $"pdfaid:conformance is '{conformance}', which is not valid for the target profile.");
        }
    }

    private Finding Error(ConformanceProfile profile, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(profile, "6.6.4"),
        Message = message,
    };
}
