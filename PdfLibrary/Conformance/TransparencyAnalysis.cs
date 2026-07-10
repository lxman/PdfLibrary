using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance;

/// <summary>
/// Per-page transparency facts backing the blending-colour clauses (ISO 19005-2, 6.2.10 and 6.2.4.3).
/// For each page it reports whether a <em>transparent object</em> is reachable, whether the page defines
/// its own blending colour space, and the device colour families of every reachable transparency group's
/// blending space.
///
/// A transparent object is present when, reachable from the page's rendered content, either:
/// an ExtGState carries <c>/SMask</c> ≠ <c>/None</c>, <c>/ca</c> &lt; 1, <c>/CA</c> &lt; 1, or a <c>/BM</c>
/// that is not <c>/Normal</c>/<c>/Compatible</c>; or a Form XObject declares a
/// <c>/Group &lt;&lt; /S /Transparency … &gt;&gt;</c>. Reachability mirrors
/// <see cref="ConformanceContext.ReferencedFonts"/> (pages → Form XObjects → tiling patterns → annotation
/// appearance streams → Type3 glyph resources, recursively, cycle-guarded on stream/resource object number).
/// An image <c>/SMask</c> is deliberately not treated as a transparency trigger here (the narrower trigger
/// set keeps the rule false-positive-free); the page's own <c>/Group</c> is not itself a transparent object.
/// </summary>
internal static class TransparencyAnalysis
{
    /// <summary>One page's transparency facts.</summary>
    internal readonly record struct PageTransparency(
        bool HasTransparentObject,
        bool PageGroupCsDefined,
        IReadOnlySet<OutputIntentColour> DeviceBlendingFamilies);

    public static IReadOnlyList<PageTransparency> Analyze(ConformanceContext context)
    {
        var results = new List<PageTransparency>();
        foreach (PdfPage page in context.Pages)
            results.Add(AnalyzePage(context, page));
        return results;
    }

    private static PageTransparency AnalyzePage(ConformanceContext context, PdfPage page)
    {
        bool hasTransparent = false;
        var blend = new HashSet<OutputIntentColour>();
        var resourceSeen = new HashSet<int>(); // resource dictionaries already walked (cycle guard)
        var streamSeen = new HashSet<int>();    // XObject / pattern streams already walked

        // Classifies a transparency group's /CS to its device family and records it as a reachable
        // blending space. Only a real transparency group (/S /Transparency) with a device (Gray/RGB/CMYK)
        // /CS contributes; a device-independent (ICC/Cal/Lab) CS resolves to None and is ignored.
        void NoteGroupCs(PdfObject? groupObj)
        {
            if (context.Resolve(groupObj) is not PdfDictionary group)
                return;
            if (context.ResolveName(group.Get("S")) != "Transparency")
                return;
            PdfObject? cs = group.Get("CS");
            if (cs is null)
                return;
            switch (ColourSpaceClassifier.DeviceFamily(context, cs))
            {
                case OutputIntentColour.Gray: blend.Add(OutputIntentColour.Gray); break;
                case OutputIntentColour.Rgb: blend.Add(OutputIntentColour.Rgb); break;
                case OutputIntentColour.Cmyk: blend.Add(OutputIntentColour.Cmyk); break;
            }
        }

        void WalkResources(PdfResources? resources)
        {
            if (resources is null)
                return;
            if (resources.Dictionary.IsIndirect && !resourceSeen.Add(resources.Dictionary.ObjectNumber))
                return;

            // A `gs` operator sets the graphics state from a named ExtGState; a transparency-bearing one
            // here means the scope can paint a transparent object.
            if (resources.GetExtGStates() is { } extGStates)
                foreach (PdfObject graphicsState in extGStates.Values)
                    if (context.Resolve(graphicsState) is PdfDictionary gsDict && IsTransparent(context, gsDict))
                        hasTransparent = true;

            // Form XObjects carry a transparency group and their own resources; image XObjects do not
            // contribute a transparent object under the trigger set used here.
            if (resources.GetXObjects() is { } xobjects)
                foreach (PdfObject xobject in xobjects.Values)
                    WalkXObject(xobject);

            // Tiling patterns are content streams painted through their own resources.
            if (resources.GetPatterns() is { } patterns)
                foreach (PdfObject pattern in patterns.Values)
                    WalkStreamResources(pattern);

            // A Type3 glyph is drawn through the font's own resources, which may host the transparent gs.
            if (resources.GetFonts() is { } fonts)
                foreach (PdfObject font in fonts.Values)
                    if (context.Resolve(font) is PdfDictionary fontDict
                        && context.ResolveName(fontDict.Get("Subtype")) == "Type3"
                        && context.Resolve(fontDict.Get("Resources")) is PdfDictionary type3Resources)
                        WalkResources(new PdfResources(type3Resources, context.Document));
        }

        void WalkXObject(PdfObject? xobjectObj)
        {
            if (context.Resolve(xobjectObj) is not PdfStream stream)
                return;
            if (context.ResolveName(stream.Dictionary.Get("Subtype")) != "Form")
                return;

            // A Form XObject with a transparency group is itself a transparent object, and its group's
            // blending colour space is a reachable blending space.
            PdfObject? group = stream.Dictionary.Get("Group");
            if (context.Resolve(group) is PdfDictionary groupDict
                && context.ResolveName(groupDict.Get("S")) == "Transparency")
            {
                hasTransparent = true;
                NoteGroupCs(group);
            }

            if (stream.IsIndirect && !streamSeen.Add(stream.ObjectNumber))
                return;
            if (context.Resolve(stream.Dictionary.Get("Resources")) is PdfDictionary resourceDict)
                WalkResources(new PdfResources(resourceDict, context.Document));
        }

        void WalkStreamResources(PdfObject? streamObj)
        {
            if (context.Resolve(streamObj) is not PdfStream stream)
                return;
            if (stream.IsIndirect && !streamSeen.Add(stream.ObjectNumber))
                return;
            if (context.Resolve(stream.Dictionary.Get("Resources")) is PdfDictionary resourceDict)
                WalkResources(new PdfResources(resourceDict, context.Document));
        }

        void WalkAppearance(PdfObject? apObj)
        {
            if (context.Resolve(apObj) is not PdfDictionary appearance)
                return;
            foreach (PdfObject state in appearance.Values) // /N, /D, /R
            {
                switch (context.Resolve(state))
                {
                    case PdfStream:
                        WalkXObject(state);
                        break;
                    case PdfDictionary subStates: // per-state appearances (e.g. button on/off)
                        foreach (PdfObject sub in subStates.Values)
                            WalkXObject(sub);
                        break;
                }
            }
        }

        // The page's own /Group is the page blending colour space — its presence-with-/CS is what the
        // clauses test, and its device family (if any) is a reachable blending space too. The page group
        // is not itself counted as a transparent object.
        PdfObject? pageGroup = page.Dictionary.Get("Group");
        bool pageGroupCsDefined = context.Resolve(pageGroup) is PdfDictionary pg && pg.Get("CS") is not null;
        NoteGroupCs(pageGroup);

        WalkResources(EffectiveResources(context, page.Dictionary));

        if (page.GetAnnotations() is { } annots)
            foreach (PdfObject entry in annots)
                if (context.Resolve(entry) is PdfDictionary annot)
                    WalkAppearance(annot.Get("AP"));

        return new PageTransparency(hasTransparent, pageGroupCsDefined, blend);
    }

    /// <summary>An ExtGState paints a transparent object when it softens, fades, or blends non-normally.</summary>
    private static bool IsTransparent(ConformanceContext context, PdfDictionary gs)
    {
        // /SMask other than the name /None applies a soft mask.
        PdfObject? smask = context.Resolve(gs.Get("SMask"));
        if (smask is not null && smask is not PdfName { Value: "None" })
            return true;

        // /ca (non-stroking) and /CA (stroking) alpha below 1 fade the paint (default is 1 = opaque).
        if (AsDouble(context.Resolve(gs.Get("ca"))) is { } ca && ca < 1.0)
            return true;
        if (AsDouble(context.Resolve(gs.Get("CA"))) is { } strokeAlpha && strokeAlpha < 1.0)
            return true;

        // A blend mode other than Normal/Compatible composites transparently. /BM may be an array naming
        // modes in preference order; any non-normal entry indicates blending.
        return BlendModeIsTransparent(context, context.Resolve(gs.Get("BM")));
    }

    private static bool BlendModeIsTransparent(ConformanceContext context, PdfObject? bm)
    {
        switch (bm)
        {
            case PdfName name:
                return name.Value is not ("Normal" or "Compatible");
            case PdfArray array:
                foreach (PdfObject entry in array)
                    if (context.Resolve(entry) is PdfName n && n.Value is not ("Normal" or "Compatible"))
                        return true;
                return false;
            default:
                return false;
        }
    }

    private static double? AsDouble(PdfObject? obj) => obj switch
    {
        PdfReal real => real.Value,
        PdfInteger integer => integer.Value,
        _ => null,
    };

    // The nearest /Resources up the page's full /Parent chain (page.GetResources() inherits only one
    // level), so resources set on a grandparent /Pages node are still reached. Cycle-guarded.
    private static PdfResources? EffectiveResources(ConformanceContext context, PdfDictionary? node)
    {
        var chainSeen = new HashSet<int>();
        while (node is not null)
        {
            if (node.IsIndirect && !chainSeen.Add(node.ObjectNumber))
                break;
            if (context.Resolve(node.Get("Resources")) is PdfDictionary resourceDict)
                return new PdfResources(resourceDict, context.Document);
            node = context.Resolve(node.Get("Parent")) as PdfDictionary;
        }
        return null;
    }
}
