using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A and PDF/X require a file identifier: the trailer must contain an <c>/ID</c> entry whose
/// value is an array of exactly two byte strings (ISO 32000-1, 7.5.5).
/// </summary>
internal sealed class FileIdentifierRule : IConformanceRule
{
    public string RuleId => "file-id";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.All;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        PdfArray? id = context.Document.Trailer.Id;

        if (id is null)
        {
            yield return Error(context.Target,
                "The trailer has no /ID entry; PDF/A and PDF/X require a file identifier.");
            yield break;
        }

        if (id.Count != 2 || id[0] is not PdfString || id[1] is not PdfString)
        {
            yield return Error(context.Target,
                $"The trailer /ID must be an array of two byte strings; found {id.Count} "
                + "element(s) of the wrong type.");
        }
    }

    private Finding Error(ConformanceProfile profile, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ClauseFor(profile),
        Message = message,
    };

    private static string ClauseFor(ConformanceProfile profile) => profile switch
    {
        ConformanceProfile.PdfA2b or ConformanceProfile.PdfA2u => "ISO 19005-2:2011, 6.1.3",
        ConformanceProfile.PdfA3b => "ISO 19005-3:2012, 6.1.3",
        ConformanceProfile.PdfX4 => "ISO 15930-7:2010, 6.2",
        _ => "—",
    };
}
