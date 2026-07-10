using System.Linq;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// Transparency blending colour space (ISO 19005-2, 6.2.10 and 6.2.4.3). When a page paints a transparent
/// object the blending must happen in a defined colour space:
/// <list type="bullet">
/// <item><b>6.2.10</b> — a page that hosts a transparent object shall not rely on an implicit device
/// blending space: if the file has no PDF/A output intent <em>and</em> the page defines no group
/// blending colour space (<c>/Group /CS</c>), the blending space is undefined.</item>
/// <item><b>6.2.4.3</b> — the blending colour space is a device colour space that the output intent does
/// not cover (DeviceRGB needs an RGB output intent, DeviceCMYK a CMYK one, DeviceGray any output intent),
/// or, with no page group colour space and no output intent, the implicit device blend of 6.2.10.</item>
/// </list>
/// Detection (see <see cref="TransparencyAnalysis"/>) covers the ExtGState soft-mask/alpha/blend-mode
/// triggers and Form-XObject transparency groups reachable from each page; it can only under-report.
/// </summary>
internal sealed class TransparencyColourRule : IConformanceRule
{
    public string RuleId => "transparency-colour";
    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        OutputIntentColour intent = context.OutputIntentColourFamily;
        IReadOnlyList<TransparencyAnalysis.PageTransparency> pages = context.PageTransparency;

        for (int page = 0; page < pages.Count; page++)
        {
            TransparencyAnalysis.PageTransparency facts = pages[page];
            if (!facts.HasTransparentObject)
                continue;

            // The page relies on the implicit (device) blending space when it defines no group colour
            // space and the file carries no output intent to supply one.
            bool implicitDeviceBlend = !facts.PageGroupCsDefined && intent == OutputIntentColour.None;

            if (implicitDeviceBlend)
                yield return Error(context.Target, "6.2.10", page,
                    "A transparent object is used on a page that defines no group blending colour space, "
                    + "and the file has no PDF/A output intent to supply one.");

            bool deviceBlendUncovered = facts.DeviceBlendingFamilies.Any(family => !Covered(family, intent));
            if (deviceBlendUncovered || implicitDeviceBlend)
                yield return Error(context.Target, "6.2.4.3", page,
                    "A transparency group blends in a device colour space that the file's output intent "
                    + "does not cover.");
        }
    }

    // The output intent covers a device blending family exactly as for direct device colour (6.2.4.3):
    // RGB needs an RGB intent, CMYK a CMYK intent, Gray any intent.
    private static bool Covered(OutputIntentColour family, OutputIntentColour intent) => family switch
    {
        OutputIntentColour.Rgb => intent == OutputIntentColour.Rgb,
        OutputIntentColour.Cmyk => intent == OutputIntentColour.Cmyk,
        OutputIntentColour.Gray => intent != OutputIntentColour.None,
        _ => true,
    };

    private Finding Error(ConformanceProfile profile, string clause, int page, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(profile, clause),
        Message = message,
        PageIndex = page,
    };
}
