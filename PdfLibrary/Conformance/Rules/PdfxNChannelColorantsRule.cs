using System.Collections.Generic;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/X-4 NChannel colorants (ISO 15930-7:2010; ISO 32000-1 8.6.6.5): an NChannel colour space must define
/// each of its spot (non-process) colorants in its <c>/Colorants</c> dictionary, so that a consumer that
/// cannot render a colorant directly can fall back to its Separation definition. Process colorants (Cyan,
/// Magenta, Yellow, Black — carried in <c>/Process</c>) and the special <c>/None</c>/<c>/All</c> names need
/// no entry. Plain DeviceN (without an NChannel <c>/Subtype</c>) has no <c>/Colorants</c> requirement.
/// </summary>
internal sealed class PdfxNChannelColorantsRule : IConformanceRule
{
    public string RuleId => "pdfx-nchannel-colorants";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfX4;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        SpotColourInventory.Collect(context, out _, out List<DeviceNDef> deviceNs);
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (DeviceNDef space in deviceNs)
        {
            if (space.Attributes is not { } attributes
                || context.ResolveName(attributes.Get("Subtype")) != "NChannel")
                continue;

            PdfDictionary? colorants = context.Resolve(attributes.Get("Colorants")) as PdfDictionary;
            foreach (string colorant in space.Colorants)
            {
                if (colorant is "None" or "All" || SpotColourInventory.ProcessColorants.Contains(colorant))
                    continue;
                if ((colorants is null || colorants.Get(new PdfName(colorant)) is null) && reported.Add(colorant))
                    yield return new Finding
                    {
                        RuleId = RuleId,
                        Severity = FindingSeverity.Error,
                        Clause = ConformanceClauses.For(context.Target, "NChannel colorants"),
                        Message = $"The NChannel spot colorant '{colorant}' has no entry in the "
                                  + "/Colorants dictionary (PDF/X-4).",
                    };
            }
        }
    }
}
