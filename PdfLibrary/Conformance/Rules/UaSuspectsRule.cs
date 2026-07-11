using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 suspect tagging (ISO 14289-1:2014, 7.1): the catalog's <c>/MarkInfo</c> dictionary must not carry
/// a <c>/Suspects</c> flag with the value <c>true</c>. A true value marks the document's tagging as unverified
/// (ISO 32000-1:2008, Table 321) — the content is tagged but the tag structure is not known to be correct — so
/// a conforming file must have <c>/Suspects</c> false or absent. Calibrated against veraPDF's PDF_UA-1 rule for
/// clause 7.1 (<c>CosDocument</c>, test 4: <c>Suspects != true</c>).
/// </summary>
internal sealed class UaSuspectsRule : IConformanceRule
{
    public string RuleId => "ua-suspects";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        if (context.Resolve(context.Catalog?.Dictionary.Get("MarkInfo")) is not PdfDictionary markInfo)
            yield break; // no /MarkInfo — nothing to check (a separate rule requires /Marked)

        if (context.Resolve(markInfo.Get("Suspects")) is PdfBoolean { Value: true })
        {
            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "7.1"),
                Message = "The document's MarkInfo /Suspects entry is true, so its tagging is marked as unverified.",
            };
        }
    }
}
