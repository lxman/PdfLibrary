using PdfLibrary.Core.Primitives;
using PdfLibrary.Metadata;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 identification (ISO 14289-1:2014, clause 5): a conforming file must declare itself through the
/// PDF/UA identification schema — the property <c>part</c> in the AIIM namespace
/// <c>http://www.aiim.org/pdfua/ns/id/</c> (prefix <c>pdfuaid</c>), carried in the XMP metadata — with the
/// value <c>1</c>. A missing metadata stream, an absent <c>pdfuaid:part</c>, or a different value is a violation.
/// </summary>
internal sealed class UaIdentificationRule : IConformanceRule
{
    private const string PdfUaIdNs = "http://www.aiim.org/pdfua/ns/id/";

    public string RuleId => "ua-identification";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        PdfStream? metadata = context.Catalog?.GetMetadata();
        if (metadata is null)
        {
            yield return Error(context,
                "The XMP metadata is missing, so the PDF/UA identification (pdfuaid:part) cannot be verified.");
            yield break;
        }

        XmpPacket packet = XmpPacket.Parse(metadata.GetDecodedData(context.Document.Decryptor));
        string? part = packet.Get(PdfUaIdNs, "part")?.Value?.Trim();

        if (part != "1")
        {
            yield return Error(context, part is null
                ? "The XMP metadata lacks the PDF/UA identification property pdfuaid:part "
                  + "(namespace http://www.aiim.org/pdfua/ns/id/), which PDF/UA-1 requires."
                : $"pdfuaid:part is '{part}', but PDF/UA-1 requires the value 1.");
        }
    }

    private Finding Error(ConformanceContext context, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "5"),
        Message = message,
    };
}
