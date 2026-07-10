using System.Collections.Generic;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 annotation requirements (ISO 14289-1:2014, 7.18). Per the clause 7.18 preamble, an annotation
/// whose Hidden flag is set or whose Subtype is Popup is out of scope and filtered out first (the preamble's
/// off-CropBox exclusion needs geometry these checks do not use and is omitted — that only under-reports).
/// <list type="number">
///   <item><b>7.18.2</b> — a TrapNet annotation is prohibited.</item>
///   <item><b>7.18.3</b> — a page that carries any in-scope annotation must set <c>/Tabs</c> to <c>/S</c>
///     (structure tab order); reported once per page.</item>
///   <item><b>7.18.5</b> (Contents) — a Link annotation must supply an alternate description in a non-empty
///     <c>/Contents</c> entry.</item>
/// </list>
/// The remaining checks are structure-nesting: they use the annotation's enclosing structure tag
/// (<see cref="StructureTree.AnnotationParentTypes"/>, the standard type of the element that references the
/// annotation via an <c>/OBJR</c>) and only run when the file is a Tagged PDF — an untagged file is already
/// reported by <see cref="UaTaggedRule"/>, so re-flagging every annotation here would be noise.
/// <list type="number">
///   <item><b>7.18.1</b> — an annotation other than Widget/Link/PrinterMark must be a direct child of an
///     <c>Annot</c> structure element.</item>
///   <item><b>7.18.4</b> — a Widget annotation must be nested in a <c>Form</c> structure element.</item>
///   <item><b>7.18.5</b> (nesting) — a Link annotation must be nested in a <c>Link</c> structure element.</item>
///   <item><b>7.18.8</b> — a PrinterMark annotation must be an artifact, i.e. not present in the structure
///     tree at all.</item>
/// </list>
/// </summary>
internal sealed class UaAnnotationRule : IConformanceRule
{
    public string RuleId => "ua-annotation";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    // A sentinel distinct from any real standard type, used when an annotation is in no /OBJR.
    private const string Untagged = "\0untagged";

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        bool tagged = context.Resolve(context.Catalog?.Dictionary.Get("StructTreeRoot")) is PdfDictionary;
        IReadOnlyDictionary<int, PdfDictionary> parents = tagged
            ? StructureTree.AnnotationParentElements(context)
            : new Dictionary<int, PdfDictionary>();

        IReadOnlyList<PdfPage> pages = context.Pages;
        for (int i = 0; i < pages.Count; i++)
        {
            PdfPage page = pages[i];

            PdfRectangle cropBox = SafeCropBox(page);
            var inScope = new List<PdfDictionary>();
            if (page.GetAnnotations() is { } annots)
            {
                foreach (PdfObject entry in annots)
                {
                    if (context.Resolve(entry) is PdfDictionary annot && IsInScope(context, annot, cropBox))
                        inScope.Add(annot);
                }
            }

            if (inScope.Count == 0)
                continue; // 7.18 does not apply to a page without in-scope annotations

            // 7.18.3 — the page must declare structure tab order.
            string? tabs = context.ResolveName(page.Dictionary.Get("Tabs"));
            if (tabs != "S")
            {
                yield return Error(context, "7.18.3", i, tabs is null
                    ? "A page with annotations does not set a /Tabs entry; PDF/UA requires /Tabs /S "
                      + "(structure tab order)."
                    : $"A page with annotations sets /Tabs /{tabs}; PDF/UA requires /Tabs /S "
                      + "(structure tab order).");
            }

            foreach (PdfDictionary annot in inScope)
            {
                string? subtype = context.ResolveName(annot.Get("Subtype"));

                // 7.18.2 — TrapNet annotations are prohibited.
                if (subtype == "TrapNet")
                    yield return Error(context, "7.18.2", i,
                        "The file contains a TrapNet annotation, which is prohibited by PDF/UA.");

                // 7.18.5 — a Link annotation needs an alternate description in /Contents.
                if (subtype == "Link" && !HasText(context, annot.Get("Contents")))
                    yield return Error(context, "7.18.5", i,
                        "A Link annotation has no alternate description (a non-empty /Contents entry).");

                // 7.18.1 — an annotation (other than a Widget, or a PrinterMark, which is an artifact) must
                // convey its meaning: either its own non-empty /Contents, or an /Alt on its enclosing
                // structure element.
                if (subtype is not ("Widget" or "PrinterMark")
                    && !HasText(context, annot.Get("Contents"))
                    && !EnclosingHasAlt(context, parents, annot))
                {
                    yield return Error(context, "7.18.1", i,
                        $"An annotation of subtype /{subtype ?? "(none)"} has neither a /Contents entry nor an "
                        + "/Alt on its enclosing structure element (no alternate description).");
                }

                if (!tagged)
                    continue; // structure-nesting checks need the logical structure tree

                string parentTag = ParentTag(context, parents, annot);
                switch (subtype)
                {
                    case "Widget" when parentTag != "Form":
                        yield return Error(context, "7.18.4", i,
                            $"A Widget annotation is nested in {Describe(parentTag)} rather than a <Form> "
                            + "structure element.");
                        break;
                    case "Link" when parentTag != "Link":
                        yield return Error(context, "7.18.5", i,
                            $"A Link annotation is nested in {Describe(parentTag)} rather than a <Link> "
                            + "structure element.");
                        break;
                    case "PrinterMark" when parentTag != Untagged:
                        yield return Error(context, "7.18.8", i,
                            "A PrinterMark annotation is present in the logical structure; it must be an "
                            + "artifact (not tagged).");
                        break;
                    case not ("Widget" or "Link" or "PrinterMark") when parentTag != "Annot":
                        yield return Error(context, "7.18.1", i,
                            $"An annotation of subtype /{subtype ?? "(none)"} is nested in {Describe(parentTag)} "
                            + "rather than being a direct child of an <Annot> structure element.");
                        break;
                }
            }
        }
    }

    /// <summary>The standard type of the structure element that references the annotation via an /OBJR, or
    /// <see cref="Untagged"/> when no structure element does.</summary>
    private static string ParentTag(ConformanceContext context,
        IReadOnlyDictionary<int, PdfDictionary> parents, PdfDictionary annot) =>
        annot.IsIndirect && parents.TryGetValue(annot.ObjectNumber, out PdfDictionary? elem)
            ? StructureTree.StandardType(context, elem) ?? "" : Untagged;

    private static string Describe(string parentTag) =>
        parentTag == Untagged ? "no structure element" : $"a <{parentTag}> element";

    /// <summary>An annotation the clause 7.18 rules apply to. The 7.18 preamble excludes a Popup, a Hidden
    /// annotation (flag bit 2), and one whose rectangle lies entirely outside the page CropBox.</summary>
    private static bool IsInScope(ConformanceContext context, PdfDictionary annot, PdfRectangle cropBox)
    {
        if (context.ResolveName(annot.Get("Subtype")) == "Popup")
            return false;
        long flags = (context.Resolve(annot.Get("F")) as PdfInteger)?.LongValue ?? 0;
        if ((flags & 2) != 0) // Hidden
            return false;
        return !IsOutsideCropBox(context, annot, cropBox);
    }

    /// <summary>True when the annotation's /Rect is a valid rectangle that lies wholly outside the CropBox.
    /// A missing or malformed /Rect is treated as in-scope (never excluded on uncertainty).</summary>
    private static bool IsOutsideCropBox(ConformanceContext context, PdfDictionary annot, PdfRectangle crop)
    {
        if (context.Resolve(annot.Get("Rect")) is not PdfArray r || r.Count != 4)
            return false;
        if (Num(context, r[0]) is not { } x0 || Num(context, r[1]) is not { } y0
            || Num(context, r[2]) is not { } x1 || Num(context, r[3]) is not { } y1)
            return false;

        (double rl, double rr) = (System.Math.Min(x0, x1), System.Math.Max(x0, x1));
        (double rb, double rt) = (System.Math.Min(y0, y1), System.Math.Max(y0, y1));
        (double cl, double cr) = (System.Math.Min(crop.X1, crop.X2), System.Math.Max(crop.X1, crop.X2));
        (double cb, double ct) = (System.Math.Min(crop.Y1, crop.Y2), System.Math.Max(crop.Y1, crop.Y2));
        return rr < cl || rl > cr || rt < cb || rb > ct;
    }

    private static double? Num(ConformanceContext context, PdfObject? obj) => context.Resolve(obj) switch
    {
        PdfInteger i => i.LongValue,
        PdfReal d => d.Value,
        _ => null,
    };

    private static PdfRectangle SafeCropBox(PdfPage page)
    {
        try { return page.GetCropBox(); }
        catch { return new PdfRectangle(double.MinValue, double.MinValue, double.MaxValue, double.MaxValue); }
    }

    /// <summary>True when the annotation's enclosing structure element carries a non-empty /Alt.</summary>
    private static bool EnclosingHasAlt(ConformanceContext context,
        IReadOnlyDictionary<int, PdfDictionary> parents, PdfDictionary annot) =>
        annot.IsIndirect && parents.TryGetValue(annot.ObjectNumber, out PdfDictionary? elem)
        && HasText(context, elem.Get("Alt"));

    /// <summary>True when the object resolves to a non-empty text string (a non-whitespace byte).</summary>
    private static bool HasText(ConformanceContext context, PdfObject? obj)
    {
        if (context.Resolve(obj) is not PdfString s)
            return false;
        foreach (byte b in s.Bytes)
            if (b != 0x00 && b != 0x20 && b != 0x09 && b != 0x0A && b != 0x0D)
                return true;
        return false;
    }

    private Finding Error(ConformanceContext context, string clause, int pageIndex, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, clause),
        Message = message,
        PageIndex = pageIndex,
    };
}
