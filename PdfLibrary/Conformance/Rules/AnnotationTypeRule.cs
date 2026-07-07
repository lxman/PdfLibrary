using System.Collections.Generic;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// Annotation types not defined in ISO 32000-1 are prohibited, and the 3D, Sound, Screen and Movie
/// types are additionally prohibited (ISO 19005-2, 6.3.1, test 1). Only the enumerated subtypes are
/// permitted; anything else — including an annotation with no /Subtype — is an error.
/// </summary>
internal sealed class AnnotationTypeRule : IConformanceRule
{
    private static readonly HashSet<string> Allowed =
    [
        "Text", "Link", "FreeText", "Line", "Square", "Circle", "Polygon", "PolyLine",
        "Highlight", "Underline", "Squiggly", "StrikeOut", "Stamp", "Caret", "Ink", "Popup",
        "FileAttachment", "Widget", "PrinterMark", "TrapNet", "Watermark", "Redact",
    ];

    public string RuleId => "annotation-type";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        foreach (PdfDictionary annot in context.Annotations)
        {
            string? subtype = context.ResolveName(annot.Get("Subtype"));
            if (subtype is not null && Allowed.Contains(subtype))
                continue;

            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "6.3.1"),
                Message = subtype is null
                    ? "An annotation has no /Subtype; only the annotation types defined in ISO 32000-1 "
                      + "(excluding 3D, Sound, Screen and Movie) are permitted."
                    : $"Annotation subtype '{subtype}' is not permitted in PDF/A.",
                ObjectNumber = annot.IsIndirect ? annot.ObjectNumber : null,
            };
        }
    }
}
