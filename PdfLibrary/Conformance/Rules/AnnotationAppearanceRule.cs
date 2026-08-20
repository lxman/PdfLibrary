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
///   <item>test 3 — when the annotation is a Widget whose field type is /Btn, its /N value shall be an
///     appearance SUBDICTIONARY of named states, not a bare appearance stream.</item>
/// </list>
/// Test 4 — the converse, that every OTHER annotation's /N shall be a stream — is not yet implemented;
/// no corpus file currently turns on it (veraPDF flags none that PdfLibrary does not already fail for
/// another clause), so it would be coverage without measurable parity.
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

            // 6.3.3-t3: a Widget whose field type is /Btn holds its appearance as a SUBDICTIONARY of
            // named states (/N << /Off 12 0 R /Yes 13 0 R >>), because a button has more than one
            // visual state to name. veraPDF's expression is
            //   AP != "N" || Subtype != "Widget" || FT != "Btn" || (N_type == "Dict" && containsAppearances)
            // so the violation is all four of: /N present, Widget, Btn, and /N not a populated dict.
            if (appearance?.Get("N") is { } normalEntry
                && subtype == "Widget"
                && EffectiveFieldType(context, annot) == "Btn")
            {
                // PdfStream does NOT derive from PdfDictionary here (both derive from PdfObject
                // directly), so this type test genuinely separates the subdictionary shape from the
                // appearance-stream shape the clause forbids — it would be a silent no-op in a model
                // where a stream IS a dictionary.
                PdfObject? normal = context.Resolve(normalEntry);
                if (normal is not PdfDictionary states || states.Count == 0)
                {
                    yield return Error(context, annot,
                        "A Widget annotation with /FT /Btn must hold its normal appearance as a "
                        + "subdictionary of named states, but /N is "
                        + DescribeNormal(normal) + ".");
                }
            }
        }
    }

    private static string DescribeNormal(PdfObject? normal) => normal switch
    {
        PdfStream => "an appearance stream",
        PdfDictionary => "an empty dictionary",   // a dict naming no states appears in no state
        null => "missing or unresolvable",
        _ => "a " + normal.GetType().Name,
    };

    /// <summary>The /FT governing this annotation — its own, or the nearest one inherited up the
    /// /Parent chain. Both shapes are real: a widget merged with its field dictionary carries /FT
    /// directly (the corpus fixture does), while a widget that is a KID of a separate field
    /// dictionary inherits it, and checking only the annotation would silently exempt every
    /// document built the second way. Cycle-guarded on /Parent, the same shape
    /// <c>UaAnnotationRule.HasEffectiveEntry</c> uses for the same chain.</summary>
    private static string? EffectiveFieldType(ConformanceContext context, PdfDictionary annot)
    {
        var seen = new HashSet<int>();
        for (PdfDictionary? field = annot; field is not null;)
        {
            if (context.ResolveName(field.Get("FT")) is { } fieldType)
                return fieldType;
            if (field.IsIndirect && !seen.Add(field.ObjectNumber))
                break;
            field = context.Resolve(field.Get("Parent")) as PdfDictionary;
        }
        return null;
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
