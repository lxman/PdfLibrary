using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 title display (ISO 14289-1:2014, 7.1): the document's <c>/ViewerPreferences</c> dictionary must
/// set <c>/DisplayDocTitle</c> to true, so a conforming reader shows the document's title (from its metadata)
/// in the title bar rather than the file name — the title bar is a primary accessibility affordance.
/// </summary>
internal sealed class UaDisplayDocTitleRule : IConformanceRule
{
    public string RuleId => "ua-display-doc-title";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        bool displayTitle =
            context.Resolve(context.Catalog?.Dictionary.Get("ViewerPreferences")) is PdfDictionary viewerPreferences
            && context.Resolve(viewerPreferences.Get("DisplayDocTitle")) is PdfBoolean { Value: true };

        if (!displayTitle)
        {
            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "7.1"),
                Message = "ViewerPreferences /DisplayDocTitle must be true so the document title (not the "
                          + "file name) is shown in the title bar (PDF/UA).",
            };
        }
    }
}
