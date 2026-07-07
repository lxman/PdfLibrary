using System.Linq;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// A stream dictionary shall not contain the <c>F</c>, <c>FFilter</c>, or <c>FDecodeParms</c> keys —
/// these reference external file data, which PDF/A prohibits (ISO 19005-2, 6.1.7.1, test 3).
/// </summary>
internal sealed class StreamExternalFileRule : IConformanceRule
{
    private static readonly string[] ForbiddenKeys = ["F", "FFilter", "FDecodeParms"];

    public string RuleId => "stream-external-file";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.All;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        foreach (PdfStream stream in context.Streams)
        {
            List<string> present = ForbiddenKeys
                .Where(k => stream.Dictionary.ContainsKey(new PdfName(k)))
                .Select(k => "/" + k)
                .ToList();

            if (present.Count > 0)
            {
                yield return new Finding
                {
                    RuleId = RuleId,
                    Severity = FindingSeverity.Error,
                    Clause = ConformanceClauses.For(context.Target, "6.1.7.1"),
                    Message = $"A stream dictionary must not contain external-file key(s): "
                              + $"{string.Join(", ", present)}.",
                    ObjectNumber = stream.IsIndirect ? stream.ObjectNumber : null,
                };
            }
        }
    }
}
