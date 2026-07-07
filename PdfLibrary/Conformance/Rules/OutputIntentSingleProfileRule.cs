using System.Linq;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// When a file's /OutputIntents array has more than one entry, they must all reference the same
/// destination output profile via the same indirect object (ISO 19005-2, 6.2.3, test 2).
/// </summary>
internal sealed class OutputIntentSingleProfileRule : IConformanceRule
{
    public string RuleId => "output-intent-single";
    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        var withProfile = context.OutputIntents.Where(oi => oi.Profile is not null).ToList();
        if (withProfile.Count <= 1)
            yield break;

        // Distinct destination profiles by indirect object number; a direct (non-indirect) profile
        // has no shared indirect key, so it counts as its own distinct sentinel (-1, -2, ...).
        int sentinel = -1;
        var keys = withProfile
            .Select(oi => oi.ProfileRef is { } r ? r.ObjectNumber : sentinel--)
            .Distinct()
            .ToList();

        if (keys.Count > 1)
        {
            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "6.2.3"),
                Message = "The /OutputIntents array has entries referencing different destination "
                          + "output profiles; they must all reference the same profile.",
            };
        }
    }
}
