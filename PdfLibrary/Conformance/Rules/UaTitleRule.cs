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
    //
    // The Array case is an ALTERNATIVES list only (2026-08-13, D2). Before the projection was fixed,
    // every rdf:Alt surfaced as LangAlt, so a multi-item Alt with no xml:lang reached the LangAlt
    // branch and yielded a title; now it projects as an Array and would fall to `_ => null`, newly
    // firing ua-title on documents that never triggered it. That would be a false positive introduced
    // by a projection refactor, which must not change any conformance verdict.
    //
    // Deliberately gated on IsAlternate rather than accepting every Array: an rdf:Seq or rdf:Bag
    // dc:title projected as an Array BEFORE this change too, and returned null then. Accepting all
    // arrays would silently stop reporting those, moving the verdict in the other direction. Only the
    // Alt case changed, so only the Alt case is restored.
    private static string? TitleText(XmpProperty? property) => property?.Kind switch
    {
        XmpValueKind.Simple => property.Value,
        XmpValueKind.LangAlt => property.LangAlt.TryGetValue("x-default", out string? text)
            ? text
            : property.LangAlt.Values.FirstOrDefault(),
        XmpValueKind.Array when property.IsAlternate =>
            property.Items.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i)),
        _ => null,
    };
}
