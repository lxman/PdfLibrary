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
