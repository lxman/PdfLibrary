using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 figures (ISO 14289-1:2014, 7.3): a structure element that carries graphical content — a
/// <c>Figure</c> or <c>Formula</c> — must provide a text alternative so assistive technology can convey it.
/// This rule flags the unambiguous case: an <c>/Alt</c> or <c>/ActualText</c> attribute that is present but
/// <b>empty</b> (e.g. <c>/Alt ()</c>). A figure that carries neither attribute is deferred, because its
/// alternative may be supplied by an <c>/ActualText</c> in the marked-content stream — which needs the
/// content walk of a later phase; flagging it here would false-positive such conformant files. Structure
/// types are resolved through <c>/RoleMap</c> by <see cref="StructureTree"/>.
/// </summary>
internal sealed class UaFigureAltRule : IConformanceRule
{
    public string RuleId => "ua-figure-alt";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        foreach (PdfDictionary element in StructureTree.Elements(context))
        {
            if (StructureTree.StandardType(context, element) is not ("Figure" or "Formula"))
                continue;

            // The unambiguous case: an /Alt is present but empty and the element offers no /ActualText at all.
            // If an /ActualText key exists (even empty) the real alternative may come from the marked-content
            // stream, which only the later content phase can confirm — so those are deferred, not flagged here.
            if (element.Get("ActualText") is null
                && element.Get("Alt") is { } alt
                && !HasText(context, alt))
            {
                yield return new Finding
                {
                    RuleId = RuleId,
                    Severity = FindingSeverity.Error,
                    Clause = ConformanceClauses.For(context.Target, "7.3"),
                    Message = "A Figure/Formula structure element has an empty /Alt and no /ActualText "
                              + "(a non-empty text alternative is required by PDF/UA).",
                    ObjectNumber = element.IsIndirect ? element.ObjectNumber : null,
                };
            }
        }
    }

    // A usable text alternative: a string with at least one non-whitespace character. An empty "()" or a
    // UTF-16 BOM-only value does not count.
    private static bool HasText(ConformanceContext context, PdfObject? value) =>
        context.Resolve(value) is PdfString s && !string.IsNullOrWhiteSpace(StripBom(s.Value));

    // /Alt text is often UTF-16BE with a leading BOM (bytes FE FF, which decode to these Latin1 chars).
    private static string StripBom(string s) =>
        s.Length >= 2 && s[0] == (char)0xFE && s[1] == (char)0xFF ? s[2..] : s;
}
