using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A and PDF/X require a file identifier: the trailer must contain a non-empty <c>/ID</c>
/// (ISO 19005-2, 6.1.3, test 1 — <c>lastID != null &amp;&amp; length &gt; 0</c>). A well-formed <c>/ID</c>
/// is an array of exactly two byte strings (ISO 32000-1, 7.5.5); anything else is flagged as a warning
/// rather than a hard failure, since the archival requirement is only that an identifier is present.
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
                "The trailer /ID must be a direct array of byte strings.");
            yield break;
        }

        // Hard requirement (matches veraPDF's non-empty lastID): the last element — the changing file
        // identifier — must be a non-empty byte string.
        if (id.Count == 0 || id[id.Count - 1] is not PdfString { Bytes.Length: > 0 })
        {
            yield return Error(context.Target,
                "The trailer /ID must contain a non-empty byte string file identifier.");
            yield break;
        }

        // Advisory: ISO 32000-1 defines /ID as exactly two byte strings.
        if (id.Count != 2 || id[0] is not PdfString || id[1] is not PdfString)
        {
            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Warning,
                Clause = ConformanceClauses.FileStructure(context.Target),
                Message = $"The trailer /ID should be an array of exactly two byte strings; found "
                          + $"{id.Count} element(s).",
            };
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
