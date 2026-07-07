using System.Collections.Generic;
using System.Linq;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// Annotation appearance requirements (ISO 19005-2, 6.3.3):
/// <list type="bullet">
///   <item>test 1 — every annotation must have a normal appearance (/AP with /N) unless it is zero-sized
///     or its subtype is Popup or Link;</item>
///   <item>test 2 — an appearance dictionary shall contain only the normal (/N) entry; any other key
///     (e.g. /D or /R), or an empty appearance dictionary, is a violation.</item>
/// </list>
/// The widget-button appearance sub-structure checks (6.3.3 tests 3 and 4) are not yet implemented.
/// </summary>
internal sealed class AnnotationAppearanceRule : IConformanceRule
{
    private static readonly PdfName NormalKey = new("N");

    public string RuleId => "annotation-appearance";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        foreach (PdfDictionary annot in context.Annotations)
        {
            string? subtype = context.ResolveName(annot.Get("Subtype"));
            var appearance = context.Resolve(annot.Get("AP")) as PdfDictionary;

            // 6.3.3-t1: a normal appearance is required unless the annotation is Popup/Link or is known to
            // be zero-sized. A missing or malformed /Rect is NOT treated as zero-sized — that would wrongly
            // exempt a malformed annotation — so the appearance stays required.
            (double Width, double Height)? size = RectSize(context, annot);
            bool exempt = subtype is "Popup" or "Link" || (size is { } s && s.Width == 0 && s.Height == 0);
            if (!exempt && appearance is null)
            {
                yield return Error(context, annot,
                    $"Annotation '{subtype ?? "(no subtype)"}' has no appearance dictionary (/AP), which PDF/A requires.");
            }

            // 6.3.3-t2: an appearance dictionary shall contain ONLY the normal (/N) entry — so an empty
            // /AP, or one carrying any key other than /N (e.g. /D or /R), is a violation.
            if (appearance is not null && !(appearance.Count == 1 && appearance.ContainsKey(NormalKey)))
            {
                yield return Error(context, annot,
                    "An annotation appearance dictionary must contain only /N, but has: "
                    + string.Join(", ", appearance.Keys.Select(k => "/" + k.Value)) + ".");
            }
        }
    }

    /// <summary>Width/height from /Rect ([llx lly urx ury]); null when /Rect is absent or malformed, so
    /// the caller does not mistake an unparseable rectangle for a zero-sized one.</summary>
    private static (double Width, double Height)? RectSize(ConformanceContext context, PdfDictionary annot)
    {
        if (context.Resolve(annot.Get("Rect")) is not PdfArray rect || rect.Count < 4)
            return null;

        double? llx = Number(context.Resolve(rect[0]));
        double? lly = Number(context.Resolve(rect[1]));
        double? urx = Number(context.Resolve(rect[2]));
        double? ury = Number(context.Resolve(rect[3]));
        if (llx is null || lly is null || urx is null || ury is null)
            return null;

        return (System.Math.Abs(urx.Value - llx.Value), System.Math.Abs(ury.Value - lly.Value));
    }

    private static double? Number(PdfObject? o) => o switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => null,
    };

    private Finding Error(ConformanceContext context, PdfDictionary annot, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "6.3.3"),
        Message = message,
        ObjectNumber = annot.IsIndirect ? annot.ObjectNumber : null,
    };
}
