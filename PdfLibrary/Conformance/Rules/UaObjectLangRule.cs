using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 natural language for outline titles (ISO 14289-1:2014, 7.2; the veraPDF <c>PDOutline</c> language
/// rule): a document outline (bookmark) entry carries a <c>/Title</c> that assistive technology reads aloud,
/// but an outline item has no place to declare its own language — so the language of the outline is taken from
/// the document catalog's default <c>/Lang</c>. A document that has outline entries but no catalog <c>/Lang</c>
/// leaves those titles' language undetermined.
/// (The sibling <c>/TU</c> form-field and <c>/Contents</c> annotation language rules also exist, but their
/// language may be supplied by the enclosing structure element — which needs the structure-context resolution
/// a later phase provides — so they are deferred to avoid false positives; the catalog-<c>/Lang</c> failures
/// they would catch are already reported here whenever the file also has outlines.)
/// </summary>
internal sealed class UaObjectLangRule : IConformanceRule
{
    public string RuleId => "ua-object-lang";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        // Outline titles have no per-item language, so they depend on the catalog default /Lang.
        if (HasLang(context, context.Catalog?.Dictionary) || !HasOutlineItems(context))
            yield break;

        yield return new Finding
        {
            RuleId = RuleId,
            Severity = FindingSeverity.Error,
            Clause = ConformanceClauses.For(context.Target, "7.2"),
            Message = "The document has outline (bookmark) entries but the catalog declares no default /Lang, so "
                      + "the natural language of the outline titles cannot be determined; PDF/UA requires it.",
        };
    }

    private static bool HasOutlineItems(ConformanceContext context) =>
        context.Resolve(context.Catalog?.Dictionary.Get("Outlines")) is PdfDictionary outlines
        && context.Resolve(outlines.Get("First")) is PdfDictionary; // at least one top-level bookmark

    private static bool HasLang(ConformanceContext context, PdfDictionary? dict) =>
        dict is not null && context.Resolve(dict.Get("Lang")) is PdfString { Value.Length: > 0 };
}
