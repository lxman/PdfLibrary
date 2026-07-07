using System.Collections.Generic;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/X-4 output intent (ISO 15930-7): the document must have exactly one OutputIntent whose /S is
/// GTS_PDFX, and that intent must embed an ICC profile via /DestOutputProfile (PDF/X-4 always requires the
/// characterization profile to be embedded — a registered /OutputConditionIdentifier alone, as PDF/X-1a/3
/// permit, is not sufficient here). The profile's ICC validity is checked by <see cref="OutputIntentProfileRule"/>.
/// </summary>
internal sealed class PdfxOutputIntentRule : IConformanceRule
{
    public string RuleId => "pdfx-output-intent";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfX4;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        List<PdfDictionary> pdfxIntents = CollectPdfxIntents(context);

        if (pdfxIntents.Count == 0)
        {
            yield return Error(context, "PDF/X-4 requires a GTS_PDFX output intent, but none is present.");
            yield break;
        }

        if (pdfxIntents.Count > 1)
        {
            yield return Error(context,
                $"PDF/X-4 permits exactly one GTS_PDFX output intent, but {pdfxIntents.Count} are present.");
        }

        PdfDictionary intent = pdfxIntents[0];
        if (context.Resolve(intent.Get("DestOutputProfile")) is not PdfStream)
        {
            yield return Error(context,
                "The GTS_PDFX output intent must embed an ICC profile via /DestOutputProfile (PDF/X-4).");
        }
    }

    private static List<PdfDictionary> CollectPdfxIntents(ConformanceContext context)
    {
        var result = new List<PdfDictionary>();
        if (context.Resolve(context.Catalog?.Dictionary.Get("OutputIntents")) is PdfArray array)
            foreach (PdfObject entry in array)
                if (context.Resolve(entry) is PdfDictionary intent
                    && context.ResolveName(intent.Get("S")) == "GTS_PDFX")
                {
                    result.Add(intent);
                }
        return result;
    }

    private Finding Error(ConformanceContext context, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "output intent"),
        Message = message,
    };
}
