using System.Collections.Generic;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// Spot colorants of DeviceN / NChannel colour spaces must be defined in the <c>/Colorants</c> dictionary, so a
/// consumer that cannot render a colorant directly can fall back to its Separation definition. Process colorants
/// (Cyan, Magenta, Yellow, Black — carried in <c>/Process</c>) and the special <c>/None</c>/<c>/All</c> names
/// need no entry. The scope is profile-aware, matching veraPDF's <c>PDDeviceN.areColorantsPresent</c> test:
/// <list type="bullet">
///   <item><b>PDF/X-4</b> (ISO 15930-7:2010; ISO 32000-1 8.6.6.5) constrains only <b>NChannel</b> spaces; a
///     plain DeviceN (no NChannel <c>/Subtype</c>) has no <c>/Colorants</c> requirement.</item>
///   <item><b>PDF/A-2/3</b> (ISO 19005-2/3 6.2.4.4) constrains <b>every</b> DeviceN space, whether or not it
///     carries an NChannel <c>/Subtype</c> — and a missing or empty <c>/Colorants</c> dictionary is itself the
///     violation.</item>
/// </list>
/// </summary>
internal sealed class PdfxNChannelColorantsRule : IConformanceRule
{
    public string RuleId => "pdfx-nchannel-colorants";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA | ConformanceProfile.PdfX4;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        SpotColourInventory.Collect(context, out _, out List<DeviceNDef> deviceNs);
        var reported = new HashSet<string>(StringComparer.Ordinal);

        // PDF/X-4 constrains only NChannel spaces; PDF/A-2/3 constrains every DeviceN space.
        bool nChannelOnly = context.Target == ConformanceProfile.PdfX4;

        foreach (DeviceNDef space in deviceNs)
        {
            if (nChannelOnly && context.ResolveName(space.Attributes?.Get("Subtype")) != "NChannel")
                continue;

            PdfDictionary? colorants = context.Resolve(space.Attributes?.Get("Colorants")) as PdfDictionary;
            foreach (string colorant in space.Colorants)
            {
                if (colorant is "None" or "All" || SpotColourInventory.ProcessColorants.Contains(colorant))
                    continue;
                if ((colorants is null || colorants.Get(new PdfName(colorant)) is null) && reported.Add(colorant))
                    yield return new Finding
                    {
                        RuleId = RuleId,
                        Severity = FindingSeverity.Error,
                        Clause = ConformanceClauses.For(context.Target, "6.2.4.4"),
                        Message = $"The spot colorant '{colorant}' used in a DeviceN or NChannel colour space "
                                  + "has no entry in the /Colorants dictionary.",
                    };
            }
        }
    }
}
