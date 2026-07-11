using System.Linq;
using PdfLibrary.Conformance.Xmp;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 natural language for non-structure-element text (ISO 14289-1:2014, 7.2) — the three remaining
/// Matterhorn CP11 "natural language cannot be determined" conditions, each of which veraPDF's PDF_UA-1
/// profile shares one escape with: a default language on the document catalog (<c>gContainsCatalogLang</c>).
/// So a catalog <c>/Lang</c> short-circuits the whole rule.
/// <list type="bullet">
///   <item><b>11-004 (7.2 t24, PDAnnot):</b> an annotation with a non-empty <c>/Contents</c> whose language
///     is undetermined — no <c>/Lang</c> on the annotation, none inherited from the structure element that
///     encloses its <c>/OBJR</c>, and none on the catalog.</item>
///   <item><b>11-005 (7.2 t25, PDFormField):</b> a form field with non-empty user-facing <c>/TU</c> text and
///     the same undetermined-language condition (checked against the field and any widget kid's structure
///     owner).</item>
///   <item><b>11-006 (7.2 t33, XMPLangAlt):</b> a language-alternative metadata property whose only value
///     carries the undefined language <c>x-default</c> (the veraPDF <c>XMPLangAlt.xDefault</c> facet) — so
///     the natural language of that metadata value is undetermined.</item>
/// </list>
/// It reads the language a structure element supplies to an annotation/widget from the structure element that
/// <em>directly</em> encloses the object's <c>/OBJR</c> (matching veraPDF: an ancestor's <c>/Lang</c> does not
/// count — corpus 7.2-t25-fail-a has a lang-bearing <c>/P</c> ancestor but a language-less <c>/Form</c>
/// container, and still fails). The companion <see cref="UaTextAttributeLangRule"/> (structure Alt/ActualText/E)
/// and <see cref="UaObjectLangRule"/> (outline titles) cover the other clause-7.2 language conditions; this
/// rule does not touch them.
/// </summary>
internal sealed class UaContentLangRule : IConformanceRule
{
    public string RuleId => "ua-content-lang";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        // gContainsCatalogLang: a default language on the catalog makes every natural language determinable,
        // so none of the three conditions can fire. This is the dominant false-positive guard.
        if (HasLang(context, context.Catalog?.Dictionary))
            yield break;

        // The structure element that DIRECTLY encloses each annotation/widget's /OBJR — the only place
        // (besides the object's own /Lang and the catalog) veraPDF's PDAnnot / PDFormField containsLang reads
        // the language from. Ancestors do not contribute.
        IReadOnlyDictionary<int, PdfDictionary> objrOwners =
            LogicalStructure.AnnotationParentElements(context.Document);

        bool StructLangCovers(int objectNumber) =>
            objrOwners.TryGetValue(objectNumber, out PdfDictionary? owner) && HasLang(context, owner);

        // 11-004 (7.2 t24, PDAnnot).
        foreach (PdfDictionary annot in context.Annotations)
        {
            if (context.Resolve(annot.Get("Contents")) is not PdfString { Value.Length: > 0 })
                continue; // Contents == null (or empty): nothing to describe
            if (HasLang(context, annot))
                continue; // containsLang: the annotation declares its own language
            if (annot.IsIndirect && StructLangCovers(annot.ObjectNumber))
                continue; // containsLang: the enclosing structure element supplies it

            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "7.2"),
                Message = "An annotation supplies a /Contents description but its natural language cannot be "
                          + "determined (no /Lang on the annotation or the document catalog).",
                ObjectNumber = annot.IsIndirect ? annot.ObjectNumber : null,
            };
        }

        // 11-005 (7.2 t25, PDFormField).
        foreach (PdfDictionary field in context.FormFields)
        {
            if (context.Resolve(field.Get("TU")) is not PdfString { Value.Length: > 0 })
                continue; // TU == null (or empty)
            if (HasLang(context, field) || FieldStructLangCovered(context, field, StructLangCovers))
                continue;

            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "7.2"),
                Message = "A form field supplies /TU (user-facing) text but its natural language cannot be "
                          + "determined (no /Lang on the field or the document catalog).",
                ObjectNumber = field.IsIndirect ? field.ObjectNumber : null,
            };
        }

        // 11-006 (7.2 t33, XMPLangAlt).
        foreach (XmpNode node in AllXmpNodes(context.XmpTree))
        {
            if (!IsXDefaultOnly(node))
                continue;

            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "7.2"),
                Message = "A language-alternative metadata property carries only an x-default "
                          + "(undefined-language) value, so its natural language cannot be determined "
                          + "(and the document catalog declares no default /Lang).",
            };
        }
    }

    // A form field and its widget annotation may be one merged object or two; the /OBJR references whichever
    // carries the appearance (the widget), so a lang-bearing structure element enclosing the field itself OR
    // any widget kid makes the field's language determinable. Lenient by design — extra coverage only ever
    // suppresses a finding, never adds one.
    private static bool FieldStructLangCovered(
        ConformanceContext context, PdfDictionary field, Func<int, bool> structLangCovers)
    {
        if (field.IsIndirect && structLangCovers(field.ObjectNumber))
            return true;
        if (context.Resolve(field.Get("Kids")) is PdfArray kids)
            foreach (PdfObject kidObj in kids)
                if (context.Resolve(kidObj) is PdfDictionary { IsIndirect: true } kid
                    && structLangCovers(kid.ObjectNumber))
                    return true;
        return false;
    }

    // A /Lang present as a (non-empty) text string. Kept lenient — a present language is treated as
    // determinable — so the rule only ever under-reports, never rejects a conformant file.
    private static bool HasLang(ConformanceContext context, PdfDictionary? dict) =>
        dict is not null && context.Resolve(dict.Get("Lang")) is PdfString { Value.Length: > 0 };

    // veraPDF XMPLangAlt.xDefault: "the Language alternative array has only a default value with an undefined
    // language (x-default)". True when the property is a language alternative (an rdf:Alt whose items all
    // carry xml:lang) with at least one item and every item's xml:lang is x-default. A specific-language item
    // — including the common [x-default, en-US] pair Acrobat writes — makes this false, so the check never
    // fires on a normally-tagged property.
    private static bool IsXDefaultOnly(XmpNode node) =>
        node.IsArrayAltText
        && node.Children.Count > 0
        && node.Children.All(item =>
            string.Equals(item.XmlLang, "x-default", StringComparison.OrdinalIgnoreCase));

    // Every XMP property node, walked depth-first: top-level properties, struct fields and array items alike,
    // so a lang-alt nested anywhere is examined (matching veraPDF, which models every XMP property).
    private static IEnumerable<XmpNode> AllXmpNodes(IReadOnlyList<XmpNode> roots)
    {
        var stack = new Stack<XmpNode>(roots);
        while (stack.Count > 0)
        {
            XmpNode node = stack.Pop();
            yield return node;
            foreach (XmpNode child in node.Children)
                stack.Push(child);
        }
    }
}
