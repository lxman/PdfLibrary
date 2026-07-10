using System.Linq;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/X-4 blend modes (ISO 15930-7:2010; ISO 32000-1 11.3.5): the <c>/BM</c> entry of a graphics-state
/// (ExtGState) dictionary may name only a standard separable or non-separable blend mode. A <c>/BM</c> that
/// is an array names the modes in preference order; every entry must still be a standard mode.
/// Detection object-scans the indirect ExtGState dictionaries that carry a <c>/BM</c>; blend modes set in a
/// direct (inline) ExtGState within a resource dictionary are a documented deferral (the rule can only
/// under-report, never raise a false positive). Each distinct non-standard mode is reported once.
/// </summary>
internal sealed class PdfxBlendModeRule : IConformanceRule
{
    // ISO 32000-1 11.3.5.2 (separable) + 11.3.5.3 (non-separable); "Compatible" is a legacy alias for Normal.
    private static readonly HashSet<string> StandardBlendModes = new(StringComparer.Ordinal)
    {
        "Normal", "Compatible", "Multiply", "Screen", "Overlay", "Darken", "Lighten",
        "ColorDodge", "ColorBurn", "HardLight", "SoftLight", "Difference", "Exclusion",
        "Hue", "Saturation", "Color", "Luminosity",
    };

    public string RuleId => "pdfx-blend-mode";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfX4;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);
        context.Document.MaterializeAllObjects();

        foreach (PdfDictionary dict in context.Document.Objects.Values.OfType<PdfDictionary>())
        {
            PdfObject? bm = context.Resolve(dict.Get("BM"));
            if (bm is null)
                continue;

            foreach (string name in BlendModeNames(context, bm))
                if (!StandardBlendModes.Contains(name) && reported.Add(name))
                    yield return Error(context, name);
        }
    }

    private static IEnumerable<string> BlendModeNames(ConformanceContext context, PdfObject bm)
    {
        switch (bm)
        {
            case PdfName name:
                yield return name.Value;
                break;
            case PdfArray array:
                foreach (PdfObject entry in array)
                    if (context.Resolve(entry) is PdfName n)
                        yield return n.Value;
                break;
        }
    }

    private Finding Error(ConformanceContext context, string blendMode) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "blend mode"),
        Message = $"The blend mode '{blendMode}' is not one of the standard ISO 32000-1 blend modes (PDF/X-4).",
    };
}
