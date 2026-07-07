using System.Collections.Generic;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/X-4 trapping (ISO 15930-7): the document information dictionary's /Trapped entry must be present
/// and explicitly True or False. The default /Unknown — or an absent entry — is not permitted.
/// </summary>
internal sealed class TrappedRule : IConformanceRule
{
    public string RuleId => "pdfx-trapped";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfX4;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        string? trapped = context.Resolve(context.Document.Trailer.Info) is PdfDictionary info
            ? context.ResolveName(info.Get("Trapped"))
            : null;

        if (trapped is not ("True" or "False"))
        {
            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "trapped"),
                Message = $"The document's /Trapped value must be explicitly True or False for PDF/X-4 "
                          + $"(found '{trapped ?? "absent"}').",
            };
        }
    }
}
