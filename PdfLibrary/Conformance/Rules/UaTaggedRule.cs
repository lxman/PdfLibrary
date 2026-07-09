using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 tagging (ISO 14289-1:2014, 7.1): a conforming file must be a Tagged PDF — the document catalog
/// must contain a <c>/StructTreeRoot</c> (a logical structure tree) and a <c>/MarkInfo</c> dictionary whose
/// <c>/Marked</c> flag is true. Without both, the content carries no machine-readable structure and cannot be
/// made accessible.
/// </summary>
internal sealed class UaTaggedRule : IConformanceRule
{
    public string RuleId => "ua-tagged";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        PdfDictionary? catalog = context.Catalog?.Dictionary;

        if (context.Resolve(catalog?.Get("StructTreeRoot")) is not PdfDictionary)
            yield return Error(context,
                "The document has no logical structure tree (catalog /StructTreeRoot); PDF/UA requires tagged content.");

        bool marked = context.Resolve(catalog?.Get("MarkInfo")) is PdfDictionary markInfo
                      && context.Resolve(markInfo.Get("Marked")) is PdfBoolean { Value: true };
        if (!marked)
            yield return Error(context,
                "The document is not flagged as tagged (catalog /MarkInfo /Marked must be true for PDF/UA).");
    }

    private Finding Error(ConformanceContext context, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "7.1"),
        Message = message,
    };
}
