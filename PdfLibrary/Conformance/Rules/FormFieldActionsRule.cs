using System.Collections.Generic;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// Interactive-form action restrictions (ISO 19005-2, 6.4.1):
/// <list type="bullet">
///   <item>test 1 — a Widget annotation shall not contain the /A or /AA keys;</item>
///   <item>test 2 — a form field dictionary shall not contain the /AA (additional-actions) key.</item>
/// </list>
/// </summary>
internal sealed class FormFieldActionsRule : IConformanceRule
{
    private static readonly PdfName ActionKey = new("A");
    private static readonly PdfName AdditionalActionsKey = new("AA");

    public string RuleId => "form-field-actions";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        // 6.4.1-t1: Widget annotations must carry neither /A nor /AA.
        foreach (PdfDictionary annot in context.Annotations)
        {
            if (context.ResolveName(annot.Get("Subtype")) != "Widget")
                continue;

            List<string> present = new();
            if (annot.ContainsKey(ActionKey)) present.Add("/A");
            if (annot.ContainsKey(AdditionalActionsKey)) present.Add("/AA");
            if (present.Count > 0)
            {
                yield return Error(context, annot,
                    $"A Widget annotation must not contain action key(s): {string.Join(", ", present)}.");
            }
        }

        // 6.4.1-t2: form field dictionaries must not carry /AA.
        foreach (PdfDictionary field in context.FormFields)
        {
            if (field.ContainsKey(AdditionalActionsKey))
            {
                yield return Error(context, field,
                    "A form field dictionary must not contain the /AA (additional-actions) key.");
            }
        }
    }

    private Finding Error(ConformanceContext context, PdfDictionary dict, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "6.4.1"),
        Message = message,
        ObjectNumber = dict.IsIndirect ? dict.ObjectNumber : null,
    };
}
