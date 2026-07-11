using System.Collections.Generic;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// Separation consistency (PDF/X-4: ISO 15930-7:2010; PDF/A-2/3: ISO 19005-2/3 6.2.4.4; both from ISO 32000-1
/// 8.6.6.4): all Separation colour spaces in a file that name the same colorant must agree on their alternate
/// colour space and tint transform, so a colorant renders identically wherever it appears. veraPDF carries the
/// identical <c>PDSeparation.areTintAndAlternateConsistent</c> test across both profiles, so this rule serves
/// both. The special <c>/None</c> and <c>/All</c> colorants are universal and exempt. A colorant is
/// inconsistent when two of its definitions differ in alternate device family
/// (<see cref="ColourSpaceClassifier"/>) or in tint-transform content
/// (<see cref="SpotColourInventory.TintTransformSignature"/>); the content-derived signature means
/// duplicated-but-identical definitions never read as inconsistent (under-reports rather than false-positives).
/// </summary>
internal sealed class PdfxSeparationConsistencyRule : IConformanceRule
{
    public string RuleId => "pdfx-separation-consistency";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA | ConformanceProfile.PdfX4;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        SpotColourInventory.Collect(context, out List<SeparationDef> separations, out _);

        var signaturesByColorant = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (SeparationDef def in separations)
        {
            if (def.Colorant is "" or "None" or "All")
                continue;
            if (!signaturesByColorant.TryGetValue(def.Colorant, out HashSet<string>? signatures))
                signaturesByColorant[def.Colorant] = signatures = new HashSet<string>(StringComparer.Ordinal);
            signatures.Add(SignatureOf(context, def));
        }

        foreach ((string colorant, HashSet<string> signatures) in signaturesByColorant)
            if (signatures.Count > 1)
                yield return new Finding
                {
                    RuleId = RuleId,
                    Severity = FindingSeverity.Error,
                    Clause = ConformanceClauses.For(context.Target, "6.2.4.4"),
                    Message = $"The separation colorant '{colorant}' is defined with an inconsistent alternate "
                              + "space or tint transform across the file.",
                };
    }

    private static string SignatureOf(ConformanceContext context, SeparationDef def) =>
        ColourSpaceClassifier.DeviceFamily(context, def.Alternate)
        + "#" + SpotColourInventory.TintTransformSignature(context, def.TintTransform);
}
