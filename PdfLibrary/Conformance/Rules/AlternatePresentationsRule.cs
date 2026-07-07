using System.Collections.Generic;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// Alternate presentations and page transitions (ISO 19005, 6.10): the document's name dictionary must
/// not contain /AlternatePresentations (test 1), and no page may contain a /PresSteps entry (test 2).
/// </summary>
internal sealed class AlternatePresentationsRule : IConformanceRule
{
    private static readonly PdfName AlternatePresentations = new("AlternatePresentations");
    private static readonly PdfName PresSteps = new("PresSteps");

    public string RuleId => "alternate-presentations";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        // 6.10-t1: no /AlternatePresentations in the document name dictionary.
        if (context.Resolve(context.Catalog?.Dictionary.Get("Names")) is PdfDictionary names
            && names.ContainsKey(AlternatePresentations))
        {
            yield return Error(context,
                "The document name dictionary must not contain an /AlternatePresentations entry.", pageIndex: null);
        }

        // 6.10-t2: no /PresSteps on any page.
        int pageIndex = 0;
        foreach (PdfPage page in context.Pages)
        {
            if (page.Dictionary.ContainsKey(PresSteps))
            {
                yield return Error(context,
                    $"Page {pageIndex + 1} must not contain a /PresSteps (presentation-steps) entry.", pageIndex);
            }
            pageIndex++;
        }
    }

    private Finding Error(ConformanceContext context, string message, int? pageIndex) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "6.10"),
        Message = message,
        PageIndex = pageIndex,
    };
}
