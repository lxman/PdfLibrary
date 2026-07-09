namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 real content (ISO 14289-1:2014, 7.1; Matterhorn checkpoint 01-004): every piece of real content
/// on a page — shown text, painted paths, shadings, and images — must be either tagged as a structure content
/// item (enclosed in a marked-content sequence carrying an <c>/MCID</c>, which links it to the logical
/// structure tree) or marked as an <c>/Artifact</c>. This rule flags a page whose content stream draws
/// something while no marked-content sequence is open at all — content that is neither tagged nor an artifact.
/// The walk (<see cref="ConformanceContext.MarkedContent"/>) carries the marked-content nesting across Form
/// XObject invocations, so a form drawn inside a tagged or artifact sequence, or tagged internally, is not
/// mistaken for untagged content.
/// </summary>
internal sealed class UaContentTaggedRule : IConformanceRule
{
    public string RuleId => "ua-content-tagged";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        MarkedContentAnalysis mc = context.MarkedContent;
        if (!mc.HasUntaggedContent)
            yield break;

        yield return new Finding
        {
            RuleId = RuleId,
            Severity = FindingSeverity.Error,
            Clause = ConformanceClauses.For(context.Target, "7.1"),
            Message = "Page content draws text or graphics that is neither tagged as a structure content item "
                      + "(no MCID reachable from the structure tree) nor marked as an /Artifact; PDF/UA requires "
                      + "all real content to be tagged or marked as an artifact.",
            PageIndex = mc.UntaggedPageIndex >= 0 ? mc.UntaggedPageIndex : null,
        };
    }
}
