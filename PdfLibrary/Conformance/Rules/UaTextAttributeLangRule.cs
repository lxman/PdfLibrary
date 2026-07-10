using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 natural language for alternative text (ISO 14289-1:2014, 7.2): when a structure element carries
/// text in an <c>/Alt</c> (alternative), <c>/ActualText</c> (replacement), or <c>/E</c> (expansion) attribute,
/// the natural language of that text must be determinable — from a <c>/Lang</c> on the element itself, on an
/// ancestor structure element, or on the document catalog (the veraPDF <c>PDStructElem</c> language rules for
/// these three attributes). This rule walks the structure tree top-down carrying whether a language is already
/// in scope, and flags an element that supplies such text with no language anywhere above it.
/// </summary>
internal sealed class UaTextAttributeLangRule : IConformanceRule
{
    public string RuleId => "ua-attribute-lang";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        // A default language on the catalog makes every element's language determinable — nothing can fail.
        if (HasLang(context, context.Catalog?.Dictionary))
            yield break;

        if (context.Resolve(context.Catalog?.Dictionary.Get("StructTreeRoot")) is not PdfDictionary root)
            yield break;

        var visited = new HashSet<int>();
        var stack = new Stack<(PdfDictionary Element, bool LangInScope)>();
        foreach (PdfDictionary child in StructureTree.ChildElements(context, root))
            stack.Push((child, false));

        for (int budget = 500_000; stack.Count > 0 && budget > 0; budget--)
        {
            (PdfDictionary element, bool langFromAncestor) = stack.Pop();
            if (element.IsIndirect && !visited.Add(element.ObjectNumber))
                continue;

            bool langInScope = langFromAncestor || HasLang(context, element);

            if (!langInScope && TextAttribute(context, element) is { } attribute)
            {
                yield return new Finding
                {
                    RuleId = RuleId,
                    Severity = FindingSeverity.Error,
                    Clause = ConformanceClauses.For(context.Target, "7.2"),
                    Message = $"A structure element supplies text in its /{attribute} attribute but its natural "
                              + "language cannot be determined (no /Lang on the element, an ancestor, or the "
                              + "document catalog); PDF/UA requires the language of such text to be determinable.",
                    ObjectNumber = element.IsIndirect ? element.ObjectNumber : null,
                };
            }

            foreach (PdfDictionary child in StructureTree.ChildElements(context, element))
                stack.Push((child, langInScope));
        }
    }

    // A /Lang present as a (non-empty) text string. Kept lenient — a present language is treated as
    // determinable — so the rule only ever under-reports, never rejects a conformant file.
    private static bool HasLang(ConformanceContext context, PdfDictionary? dict) =>
        dict is not null && context.Resolve(dict.Get("Lang")) is PdfString { Value.Length: > 0 };

    // The first of /Alt, /ActualText, /E that carries non-empty text on the element, or null. Returns the key
    // name for the message.
    private static string? TextAttribute(ConformanceContext context, PdfDictionary element)
    {
        foreach (string key in new[] { "Alt", "ActualText", "E" })
            if (context.Resolve(element.Get(key)) is PdfString { Value.Length: > 0 })
                return key;
        return null;
    }
}
