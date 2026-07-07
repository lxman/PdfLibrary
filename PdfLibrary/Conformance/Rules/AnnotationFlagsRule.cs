using System.Collections.Generic;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// Annotation flag requirements (ISO 19005-2, 6.3.2):
/// <list type="bullet">
///   <item>test 1 — every annotation except Popup must carry the /F flags entry;</item>
///   <item>test 2 — when /F is present, the Print flag must be set and the Hidden, Invisible, NoView and
///     ToggleNoView flags must be clear.</item>
/// </list>
/// </summary>
internal sealed class AnnotationFlagsRule : IConformanceRule
{
    // Annotation flag bit masks (ISO 32000-1, Table 165).
    private const int Invisible = 1 << 0;    // 1
    private const int Hidden = 1 << 1;       // 2
    private const int Print = 1 << 2;        // 4
    private const int NoView = 1 << 5;       // 32
    private const int ToggleNoView = 1 << 8; // 256

    public string RuleId => "annotation-flags";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        foreach (PdfDictionary annot in context.Annotations)
        {
            bool isPopup = context.ResolveName(annot.Get("Subtype")) == "Popup";
            PdfObject? f = context.Resolve(annot.Get("F"));

            // 6.3.2-t1: a non-Popup annotation must have the /F entry — any present value counts, so a
            // (malformed) non-integer /F still satisfies presence and must not be reported as missing.
            if (!isPopup && f is null)
            {
                yield return Error(context, annot, "A non-Popup annotation is missing the required /F flags entry.");
                continue;
            }

            // 6.3.2-t2: when /F carries a numeric value, Print must be set and Hidden/Invisible/NoView/
            // ToggleNoView clear. A real is read for its integer value; a non-numeric /F cannot be checked.
            int? flags = f switch { PdfInteger i => i.Value, PdfReal r => (int)r.Value, _ => null };
            if (flags is { } bits
                && ((bits & Print) != Print || (bits & Hidden) != 0 || (bits & Invisible) != 0
                    || (bits & NoView) != 0 || (bits & ToggleNoView) != 0))
            {
                yield return Error(context, annot,
                    $"Annotation /F flags (0x{bits:X}) must set Print and clear Hidden, Invisible, NoView and ToggleNoView.");
            }
        }
    }

    private Finding Error(ConformanceContext context, PdfDictionary annot, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "6.3.2"),
        Message = message,
        ObjectNumber = annot.IsIndirect ? annot.ObjectNumber : null,
    };
}
