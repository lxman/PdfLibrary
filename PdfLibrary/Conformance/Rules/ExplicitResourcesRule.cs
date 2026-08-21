using System.Collections.Generic;
using PdfLibrary.Content;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// ISO 19005-2 6.2.2 test 2 — "A content stream that references other objects, such as images and
/// fonts that are necessary to fully render or process the stream, shall have an explicitly
/// associated Resources dictionary" (ISO 32000-1:2008, 7.8.3).
///
/// <para>The predicate is veraPDF's <c>inheritedResourceNames == ''</c>: a name the stream references
/// that is absent from the /Resources dictionary DIRECTLY associated with that stream, yet resolvable
/// through the fallback a consumer would use. Requiring resolvability is deliberate — a name that
/// resolves nowhere is a different defect, and staying silent on it is the lower-false-positive
/// reading as well as the faithful one.</para>
///
/// <para>Device colour operators (<c>rg</c>/<c>g</c>/<c>k</c>) and the device colour space names are
/// NOT resource references. The corpus pins this precisely: 6-2-2-t04-fail-e and -pass-b are the same
/// structure — a Form XObject with no /Resources — and differ only in whether the stream names a
/// resource.</para>
///
/// <para>Deferred, deliberately: /Properties (BDC/DP) and inline-image /CS colour spaces. No corpus
/// fixture needs either and both are pure false-positive surface.</para>
/// </summary>
internal sealed class ExplicitResourcesRule : IConformanceRule
{
    public string RuleId => "explicit-resources";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    /// <summary>Colour space names that name a device space rather than a /ColorSpace resource.</summary>
    private static readonly HashSet<string> DeviceColourSpaces =
        new(System.StringComparer.Ordinal) { "DeviceGray", "DeviceRGB", "DeviceCMYK", "Pattern" };

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        var pageIndex = 0;
        foreach (PdfPage page in context.Pages)
        {
            PdfResources? direct = ResourcesOf(context, page.Dictionary);
            PdfResources? inherited = InheritedResources(context, page.Dictionary);

            IReadOnlyList<PdfOperator> ops;
            try { ops = context.PageContentOperators(page); }
            catch { pageIndex++; continue; }

            List<string> offenders = Offenders(context, ops, direct, inherited);
            if (offenders.Count > 0)
                yield return Make(context, pageIndex, page.Dictionary, offenders);

            pageIndex++;
        }
    }

    /// <summary>The /Resources dictionary DIRECTLY associated with a node — never inherited.</summary>
    private static PdfResources? ResourcesOf(ConformanceContext context, PdfDictionary? node) =>
        node is not null && context.Resolve(node.Get("Resources")) is PdfDictionary dict
            ? new PdfResources(dict, context.Document)
            : null;

    /// <summary>
    /// The nearest /Resources strictly ABOVE a page, up its full /Parent chain. Mirrors
    /// ReferencedFontWalker.EffectiveResources (PdfPage.GetResources() inherits only one level, and
    /// reads an injected parent node rather than the /Parent key, so it is unusable here).
    /// Cycle-guarded.
    /// </summary>
    private static PdfResources? InheritedResources(ConformanceContext context, PdfDictionary page)
    {
        var seen = new HashSet<int>();
        PdfDictionary? node = context.Resolve(page.Get("Parent")) as PdfDictionary;
        while (node is not null)
        {
            if (node.IsIndirect && !seen.Add(node.ObjectNumber))
                break;
            if (context.Resolve(node.Get("Resources")) is PdfDictionary dict)
                return new PdfResources(dict, context.Document);
            node = context.Resolve(node.Get("Parent")) as PdfDictionary;
        }
        return null;
    }

    /// <summary>Names referenced by these operators that are absent from <paramref name="direct"/>
    /// but present in <paramref name="inherited"/>, in first-seen order.</summary>
    private static List<string> Offenders(
        ConformanceContext context, IReadOnlyList<PdfOperator> ops,
        PdfResources? direct, PdfResources? inherited)
    {
        var offenders = new List<string>();
        var seen = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (PdfOperator op in ops)
        {
            if (ResourceReference(op) is not { } reference)
                continue;
            (string category, string name) = reference;
            if (Contains(context, direct, category, name))
                continue;
            if (!Contains(context, inherited, category, name))
                continue; // resolves nowhere — not an INHERITED name
            if (seen.Add($"{category}/{name}"))
                offenders.Add(name);
        }

        return offenders;
    }

    /// <summary>The (category, name) a resource-referencing operator names, or null.</summary>
    private static (string Category, string Name)? ResourceReference(PdfOperator op)
    {
        switch (op.Name)
        {
            case "Tf" when NameOperand(op, 0) is { } font:
                return ("Font", font);
            case "Do" when NameOperand(op, 0) is { } xobject:
                return ("XObject", xobject);
            case "sh" when NameOperand(op, 0) is { } shading:
                return ("Shading", shading);
            case "gs" when NameOperand(op, 0) is { } gstate:
                return ("ExtGState", gstate);
            case "cs" or "CS" when NameOperand(op, 0) is { } space && !DeviceColourSpaces.Contains(space):
                return ("ColorSpace", space);
            // scn/SCN name a Pattern only through a trailing name operand; the numeric forms are
            // colour components in the current space and reference nothing.
            case "scn" or "SCN" when TrailingNameOperand(op) is { } pattern:
                return ("Pattern", pattern);
            default:
                return null;
        }
    }

    private static string? NameOperand(PdfOperator op, int index) =>
        op.Operands.Count > index && op.Operands[index] is PdfName name ? name.Value : null;

    private static string? TrailingNameOperand(PdfOperator op) =>
        op.Operands.Count > 0 && op.Operands[^1] is PdfName name ? name.Value : null;

    /// <summary>True when the resources carry <paramref name="name"/> under <paramref name="category"/>.</summary>
    private static bool Contains(
        ConformanceContext context, PdfResources? resources, string category, string name) =>
        resources is not null
        && context.Resolve(resources.Dictionary.Get(category)) is PdfDictionary sub
        && sub.TryGetValue(new PdfName(name), out _);

    private Finding Make(
        ConformanceContext context, int pageIndex, PdfDictionary? owner, IReadOnlyList<string> names) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "6.2.2"),
        Message = $"A content stream refers to resource(s) {string.Join(", ", names)} not defined in an "
                  + "explicitly associated Resources dictionary.",
        PageIndex = pageIndex,
        ObjectNumber = owner is { IsIndirect: true } ? owner.ObjectNumber : null,
    };
}
