using System.Linq;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Metadata;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 document title (ISO 14289-1:2014, 7.1): the XMP metadata must carry a non-empty document title
/// (<c>dc:title</c>), which — together with <see cref="UaDisplayDocTitleRule"/> — is what a reader announces
/// for the document. A missing metadata stream or an absent/empty <c>dc:title</c> is a violation.
/// </summary>
internal sealed class UaTitleRule : IConformanceRule
{
    private const string DublinCoreNs = "http://purl.org/dc/elements/1.1/";

    public string RuleId => "ua-title";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        PdfStream? metadata = context.Catalog?.GetMetadata();
        string? title = metadata is null
            ? null
            : TitleText(XmpPacket.Parse(metadata.GetDecodedData(context.Document.Decryptor)).Get(DublinCoreNs, "title"));

        if (string.IsNullOrWhiteSpace(title))
        {
            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "7.1"),
                Message = "The XMP metadata has no document title (dc:title); PDF/UA requires a title.",
            };
        }
    }

    // dc:title is normally a language alternative; accept the x-default (or any) entry, or a simple value.
    private static string? TitleText(XmpProperty? property) => property?.Kind switch
    {
        XmpValueKind.Simple => property.Value,
        XmpValueKind.LangAlt => property.LangAlt.TryGetValue("x-default", out string? text)
            ? text
            : property.LangAlt.Values.FirstOrDefault(),
        _ => null,
    };
}
