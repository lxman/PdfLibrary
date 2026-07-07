using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A and PDF/X require a file identifier: the trailer must contain an <c>/ID</c> entry whose
/// value is a direct array of exactly two byte strings (ISO 32000-1, 7.5.5).
/// </summary>
internal sealed class FileIdentifierRule : IConformanceRule
{
    public string RuleId => "file-id";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.All;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        PdfDictionary trailer = context.Document.Trailer.Dictionary;

        if (!trailer.ContainsKey(new PdfName("ID")))
        {
            yield return Error(context.Target,
                "The trailer has no /ID entry; PDF/A and PDF/X require a file identifier.");
            yield break;
        }

        // Trailer.Id yields the array only when /ID is a direct PdfArray; an indirect reference or a
        // non-array value leaves it null even though the key is present.
        PdfArray? id = context.Document.Trailer.Id;
        if (id is null)
        {
            yield return Error(context.Target,
                "The trailer /ID must be a direct array of two byte strings.");
            yield break;
        }

        if (id.Count != 2 || id[0] is not PdfString || id[1] is not PdfString)
        {
            yield return Error(context.Target,
                $"The trailer /ID must be an array of exactly two byte strings; found {id.Count} element(s).");
        }
    }

    private Finding Error(ConformanceProfile profile, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.FileStructure(profile),
        Message = message,
    };
}
