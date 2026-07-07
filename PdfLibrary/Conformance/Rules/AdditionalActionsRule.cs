using System.Collections.Generic;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// Additional-actions (trigger-event) restrictions (ISO 19005-2, 6.5.2): neither the document catalog
/// (test 1) nor any page (test 2) may contain an /AA additional-actions dictionary.
/// </summary>
internal sealed class AdditionalActionsRule : IConformanceRule
{
    private static readonly PdfName AA = new("AA");

    public string RuleId => "additional-actions";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        // 6.5.2-t1: the catalog must not have /AA.
        if (context.Catalog?.Dictionary is { } catalog && catalog.ContainsKey(AA))
        {
            yield return Error(context,
                "The document catalog must not contain an /AA (additional-actions) entry.", pageIndex: null);
        }

        // 6.5.2-t2: no page may have /AA.
        int pageIndex = 0;
        foreach (PdfPage page in context.Pages)
        {
            if (page.Dictionary.ContainsKey(AA))
            {
                yield return Error(context,
                    $"Page {pageIndex + 1} must not contain an /AA (additional-actions) entry.", pageIndex);
            }
            pageIndex++;
        }
    }

    private Finding Error(ConformanceContext context, string message, int? pageIndex) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "6.5.2"),
        Message = message,
        PageIndex = pageIndex,
    };
}
