using System.Collections.Generic;
using System.Linq;
using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core;
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

    /// <summary>Recursion cap for <see cref="WalkStream"/>, mirroring <c>ContentWalk.MaxFormDepth</c>.</summary>
    private const int MaxDepth = 24;

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

            foreach (Finding finding in WalkStream(
                         context, ops, direct, inherited, pageIndex, page.Dictionary, 0, new HashSet<int>()))
            {
                yield return finding;
            }

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

    /// <summary>
    /// Findings for this stream and every stream it reaches. A form's DIRECT resources are its own
    /// /Resources; its fallback is the invoking scope's EFFECTIVE resources (direct, else inherited) —
    /// what a consumer would actually resolve against. Cycle-guarded on the active Do/Tf path — one
    /// <paramref name="activeObjects"/> set shared by both, since PDF object numbers are unique across
    /// all indirect objects in a document, a Form stream and a Type3 font dictionary can never collide —
    /// and depth-capped, mirroring ContentWalk.
    /// </summary>
    private List<Finding> WalkStream(
        ConformanceContext context, IReadOnlyList<PdfOperator> ops,
        PdfResources? direct, PdfResources? inherited,
        int pageIndex, PdfDictionary? owner, int depth, HashSet<int> activeObjects)
    {
        var findings = new List<Finding>();
        if (depth > MaxDepth)
            return findings;

        List<string> offenders = Offenders(context, ops, direct, inherited);
        if (offenders.Count > 0)
            findings.Add(Make(context, pageIndex, owner, offenders));

        PdfResources? effective = direct ?? inherited;

        foreach (PdfOperator op in ops)
        {
            if (op is not InvokeXObjectOperator invoke)
                continue;
            if (effective?.GetXObject(invoke.XObjectName) is not { } form)
                continue;
            if (context.ResolveName(form.Dictionary.Get("Subtype")) != "Form")
                continue;
            if (form.IsIndirect && !activeObjects.Add(form.ObjectNumber))
                continue; // already on the active Do path — a cycle

            byte[] data;
            try { data = form.GetDecodedData(context.Document.Decryptor); }
            catch { data = []; }

            if (data.Length > 0)
            {
                List<PdfOperator>? formOps = null;
                try { formOps = PdfContentParser.Parse(data); }
                catch { /* unparseable form contributes nothing */ }

                if (formOps is not null)
                {
                    findings.AddRange(WalkStream(
                        context, formOps, ResourcesOf(context, form.Dictionary), effective,
                        pageIndex, form.Dictionary, depth + 1, activeObjects));
                }
            }

            if (form.IsIndirect)
                activeObjects.Remove(form.ObjectNumber);
        }

        foreach (PdfOperator op in ops)
        {
            if (op.Name == "Tf" && NameOperand(op, 0) is { } fontName)
            {
                foreach (Finding finding in WalkType3(
                             context, effective, fontName, pageIndex, depth, activeObjects))
                {
                    findings.Add(finding);
                }
            }
        }

        return findings;
    }

    /// <summary>
    /// A Type3 glyph procedure's DIRECTLY associated resources are the Type3 FONT dictionary's
    /// /Resources (ISO 32000-1 9.6.5); absent that, a consumer falls back to the invoking scope's.
    /// Every charproc is walked, not only the glyphs shown — the font is reached, which is what
    /// veraPDF models, and the corpus fixture's unused glyph carries the same defect as its used ones.
    /// Cycle-guarded on <paramref name="activeObjects"/> exactly like the form path: a charproc whose
    /// content re-selects the same font via <c>Tf</c> is skipped rather than re-walked, since without
    /// this guard every charproc walked at every recursion level makes the work exponential in depth,
    /// not linear — <see cref="MaxDepth"/> alone would still terminate it, but not in useful time.
    /// </summary>
    private List<Finding> WalkType3(
        ConformanceContext context, PdfResources? effective, string fontName,
        int pageIndex, int depth, HashSet<int> activeObjects)
    {
        var findings = new List<Finding>();

        if (effective is null
            || context.Resolve(effective.Dictionary.Get("Font")) is not PdfDictionary fonts
            || !fonts.TryGetValue(new PdfName(fontName), out PdfObject? fontObj)
            || context.Resolve(fontObj) is not PdfDictionary font
            || context.ResolveName(font.Get("Subtype")) != "Type3"
            || context.Resolve(font.Get("CharProcs")) is not PdfDictionary charProcs)
        {
            return findings;
        }

        if (font.IsIndirect && !activeObjects.Add(font.ObjectNumber))
            return findings; // already on the active Tf path — a cycle

        PdfResources? direct = ResourcesOf(context, font);

        foreach (PdfObject value in charProcs.Values.ToList())
        {
            if (context.Resolve(value) is not PdfStream proc)
                continue;

            byte[] data;
            try { data = proc.GetDecodedData(context.Document.Decryptor); }
            catch { continue; }
            if (data.Length == 0)
                continue;

            List<PdfOperator> ops;
            try { ops = PdfContentParser.Parse(data); }
            catch { continue; }

            findings.AddRange(WalkStream(
                context, ops, direct, effective, pageIndex, font, depth + 1, activeObjects));
        }

        if (font.IsIndirect)
            activeObjects.Remove(font.ObjectNumber);

        return findings;
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
        ObjectNumber = OwnerObjectNumber(context, owner),
    };

    /// <summary>
    /// The reportable object number for a scope's owner. A page's dictionary IS the indirect object —
    /// <c>PdfDocument.AddObject</c> and the parser stamp IsIndirect/ObjectNumber directly onto it — so
    /// <c>owner.IsIndirect</c> covers that case. A Form XObject's dictionary is embedded inside its
    /// enclosing stream; indirect identity is stamped on the PdfStream wrapper, never on its nested
    /// Dictionary, so a form's owning stream is recovered by reference-matching against
    /// <see cref="ConformanceContext.Streams"/> — the same reason ImageDictionaryRule and
    /// ProhibitedXObjectRule report a stream's own ObjectNumber rather than its Dictionary's.
    /// </summary>
    private static int? OwnerObjectNumber(ConformanceContext context, PdfDictionary? owner)
    {
        if (owner is null)
            return null;
        if (owner.IsIndirect)
            return owner.ObjectNumber;

        foreach (PdfStream stream in context.Streams)
        {
            if (ReferenceEquals(stream.Dictionary, owner))
                return stream.IsIndirect ? stream.ObjectNumber : null;
        }
        return null;
    }
}
