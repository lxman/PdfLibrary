using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 figures (ISO 14289-1:2014, 7.3): a structure element that carries graphical content — a
/// <c>Figure</c> or <c>Formula</c> — must provide a text alternative so assistive technology can convey it.
/// A figure satisfies this with a non-empty element-level <c>/Alt</c>, or with an <c>/ActualText</c> key (the
/// corpus treats an <c>/ActualText</c> present on the element as sufficient even when empty), or with an
/// <c>/ActualText</c> in the marked-content stream reached by the figure's <c>/MCID</c>. This rule flags:
/// <list type="bullet">
///   <item>an <c>/Alt</c> that is present but <b>empty</b> with no <c>/ActualText</c> key at all; and</item>
///   <item>a figure carrying <b>neither</b> <c>/Alt</c> nor <c>/ActualText</c> whose marked-content sequence
///     also supplies no <c>/ActualText</c> (checked against <see cref="ConformanceContext.MarkedContent"/>).</item>
/// </list>
/// Structure types are resolved through <c>/RoleMap</c> by <see cref="LogicalStructure"/>.
/// </summary>
internal sealed class UaFigureAltRule : IConformanceRule
{
    public string RuleId => "ua-figure-alt";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        foreach (PdfDictionary element in LogicalStructure.Elements(context.Document))
        {
            if (LogicalStructure.StandardType(context.Document, element) is not ("Figure" or "Formula"))
                continue;

            bool hasActualTextKey = element.Get("ActualText") is not null;

            // Case 1: an /Alt is present but empty and there is no /ActualText key. (An /ActualText key present,
            // even empty, is accepted — the corpus treats it as the content-stream mechanism.)
            if (!hasActualTextKey && element.Get("Alt") is { } alt && !HasText(context, alt))
            {
                yield return Flag(context, element,
                    "A Figure/Formula structure element has an empty /Alt and no /ActualText "
                    + "(a non-empty text alternative is required by PDF/UA).");
                continue;
            }

            // Case 2: neither /Alt nor /ActualText on the element. The alternative could still come from an
            // /ActualText in the figure's marked-content sequence; flag only when that is absent too.
            if (!hasActualTextKey && element.Get("Alt") is null
                && !AnyMcidHasContentActualText(context, element))
            {
                yield return Flag(context, element,
                    "A Figure/Formula structure element has no /Alt or /ActualText, and its marked-content "
                    + "sequence supplies no /ActualText (a text alternative is required by PDF/UA).");
            }
        }
    }

    private Finding Flag(ConformanceContext context, PdfDictionary element, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "7.3"),
        Message = message,
        ObjectNumber = element.IsIndirect ? element.ObjectNumber : null,
    };

    // True when any MCID reachable from the element's /K carries a content-stream /ActualText. MCIDs are
    // page-scoped but the walk's set is document-wide; treating a match as "has alt" can only suppress a
    // finding (never invent one), so it stays on the safe side of the zero-false-positive invariant.
    private static bool AnyMcidHasContentActualText(ConformanceContext context, PdfDictionary element)
    {
        IReadOnlySet<int> withActualText = context.MarkedContent.ActualTextMcids;
        if (withActualText.Count == 0)
            return false;
        foreach (int mcid in ElementMcids(context, element))
            if (withActualText.Contains(mcid))
                return true;
        return false;
    }

    // The integer MCIDs a structure element owns: a direct integer in /K, integers in a /K array, or the
    // /MCID of a marked-content reference (/MCR) dictionary in /K.
    private static IEnumerable<int> ElementMcids(ConformanceContext context, PdfDictionary element)
    {
        switch (context.Resolve(element.Get("K")))
        {
            case PdfInteger single:
                yield return single.Value;
                break;
            case PdfArray kids:
                foreach (PdfObject kid in kids)
                {
                    switch (context.Resolve(kid))
                    {
                        case PdfInteger i:
                            yield return i.Value;
                            break;
                        case PdfDictionary mcr when context.Resolve(mcr.Get("MCID")) is PdfInteger m:
                            yield return m.Value;
                            break;
                    }
                }
                break;
            case PdfDictionary mcr when context.Resolve(mcr.Get("MCID")) is PdfInteger m:
                yield return m.Value;
                break;
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
