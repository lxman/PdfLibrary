using System.Collections.Generic;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// Document-level interactive-form constraints:
/// <list type="bullet">
///   <item>6.4.1-t3 — the interactive form dictionary's /NeedAppearances flag must be absent or false;</item>
///   <item>6.4.2-t1 — the form dictionary shall not contain /XFA (XFA forms are prohibited);</item>
///   <item>6.4.2-t2 — the document catalog shall not set /NeedsRendering true.</item>
/// </list>
/// </summary>
internal sealed class FormConfigRule : IConformanceRule
{
    public string RuleId => "form-config";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        PdfDictionary? acroForm = context.Catalog?.GetAcroForm();
        if (acroForm is not null)
        {
            if (context.Resolve(acroForm.Get("NeedAppearances")) is PdfBoolean { Value: true })
            {
                yield return Error(context, "6.4.1",
                    "The interactive form dictionary sets /NeedAppearances true; it must be absent or false.");
            }

            if (acroForm.ContainsKey(new PdfName("XFA")))
            {
                yield return Error(context, "6.4.2",
                    "The interactive form dictionary must not contain the /XFA key (XFA forms are prohibited).");
            }
        }

        if (context.Catalog?.Dictionary is { } catalog
            && context.Resolve(catalog.Get("NeedsRendering")) is PdfBoolean { Value: true })
        {
            yield return Error(context, "6.4.2",
                "The document catalog sets /NeedsRendering true, which is prohibited in PDF/A.");
        }
    }

    private Finding Error(ConformanceContext context, string clause, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, clause),
        Message = message,
    };
}
