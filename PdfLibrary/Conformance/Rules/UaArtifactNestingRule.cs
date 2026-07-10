namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 artifact separation (ISO 14289-1:2014, 7.1; Matterhorn checkpoints 01-005 / 01-006): content
/// marked as an <c>/Artifact</c> and real (tagged) content must not be nested within one another — an artifact
/// cannot live inside a tagged marked-content sequence, nor tagged content inside an artifact. They must be
/// sibling marked-content sequences. This rule reports a page whose content stream nests the two, detected by
/// the marked-content walk (<see cref="ConformanceContext.MarkedContent"/>).
/// </summary>
internal sealed class UaArtifactNestingRule : IConformanceRule
{
    public string RuleId => "ua-artifact-nesting";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        MarkedContentAnalysis mc = context.MarkedContent;
        if (!mc.HasArtifactNesting)
            yield break;

        yield return new Finding
        {
            RuleId = RuleId,
            Severity = FindingSeverity.Error,
            Clause = ConformanceClauses.For(context.Target, "7.1"),
            Message = "An /Artifact marked-content sequence and tagged (structure) content are nested within "
                      + "each other; PDF/UA requires artifacts and real content to be separate, sibling "
                      + "marked-content sequences.",
            PageIndex = mc.NestingPageIndex >= 0 ? mc.NestingPageIndex : null,
        };
    }
}
