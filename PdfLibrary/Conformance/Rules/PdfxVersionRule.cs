using System;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Metadata;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/X-4 version identification (ISO 15930-7:2010, 6.1 / Annex A): a conforming file must declare its
/// conformance level through the PDF/X identification schema — the <c>GTS_PDFXVersion</c> property in the
/// NPES namespace <c>http://www.npes.org/pdfx/ns/id/</c> (prefix <c>pdfxid</c>), carried in the XMP
/// metadata — and its value must begin with "PDF/X-4" (covering both plain "PDF/X-4" and the
/// external-profile variant "PDF/X-4p"). A missing metadata stream, an absent <c>GTS_PDFXVersion</c>, or a
/// value naming a different PDF/X flavour makes the file non-conformant. The check is on the normative
/// NPES schema, not Adobe's legacy <c>http://ns.adobe.com/pdfx/1.3/</c> schema (which conformant writers
/// emit alongside it but which does not itself satisfy ISO 15930-7 identification).
/// </summary>
internal sealed class PdfxVersionRule : IConformanceRule
{
    // The PDF/X identification schema namespace registered by ISO 15930-7 (NPES).
    private const string PdfxIdNs = "http://www.npes.org/pdfx/ns/id/";
    private const string VersionProperty = "GTS_PDFXVersion";

    public string RuleId => "pdfx-version";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfX4;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        PdfStream? metadata = context.Catalog?.GetMetadata();
        if (metadata is null)
        {
            yield return Error(context,
                "The XMP metadata is missing, so the PDF/X-4 version identification "
                + "(pdfxid:GTS_PDFXVersion) cannot be verified.");
            yield break;
        }

        XmpPacket packet = XmpPacket.Parse(metadata.GetDecodedData(context.Document.Decryptor));
        string? version = packet.Get(PdfxIdNs, VersionProperty)?.Value?.Trim();

        if (version is null)
        {
            yield return Error(context,
                "The XMP metadata lacks the PDF/X identification property pdfxid:GTS_PDFXVersion "
                + "(namespace http://www.npes.org/pdfx/ns/id/), which PDF/X-4 requires.");
            yield break;
        }

        // "PDF/X-4" and "PDF/X-4p" (external output-intent profile) are the two ISO 15930-7 values.
        if (!version.StartsWith("PDF/X-4", StringComparison.Ordinal))
        {
            yield return Error(context,
                $"pdfxid:GTS_PDFXVersion is '{version}', but PDF/X-4 requires a value beginning with 'PDF/X-4'.");
        }
    }

    private Finding Error(ConformanceContext context, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "version identification"),
        Message = message,
    };
}
