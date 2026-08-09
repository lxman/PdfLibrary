using System.Linq;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Conformance;

/// <summary>
/// The single definition of "a font this document references for rendering" — page resources, Form
/// XObjects, tiling patterns, annotation appearance streams, Type3 glyph resources and ExtGState
/// /Font entries, walked recursively and cycle-guarded, following each Type0 to its descendant
/// CIDFont.
///
/// <para>Lifted out of <see cref="ConformanceContext"/> so the font inventory and the conformance
/// rules cannot disagree about which fonts exist. A divergence here surfaces to the user as a
/// finding against a font the Fonts panel does not list, which is unexplainable from the UI.</para>
/// </summary>
internal static class ReferencedFontWalker
{
    public static IReadOnlyList<PdfDictionary> Collect(
        PdfDocument document,
        IReadOnlyList<PdfPage> pages,
        IReadOnlyList<PdfDictionary> annotations,
        PdfCatalog? catalog)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(annotations);

        PdfObject? Resolve(PdfObject? obj) =>
            obj is PdfIndirectReference reference ? document.ResolveReference(reference) : obj;

        string? ResolveName(PdfObject? obj) => (Resolve(obj) as PdfName)?.Value;

        var fonts = new List<PdfDictionary>();
        var fontSeen = new HashSet<int>();      // font object numbers already collected
        var resourceSeen = new HashSet<int>();  // resource dictionaries already walked (cycle guard)
        var streamSeen = new HashSet<int>();    // XObject / pattern streams already walked

        void AddFont(PdfObject? fontObj)
        {
            if (Resolve(fontObj) is not PdfDictionary font)
                return;
            if (font.IsIndirect && !fontSeen.Add(font.ObjectNumber))
                return;

            fonts.Add(font);

            switch (ResolveName(font.Get("Subtype")))
            {
                // A composite font's program lives on its descendant CIDFont — reach it so embedding is checked.
                case "Type0" when Resolve(font.Get("DescendantFonts")) is PdfArray descendants && descendants.Count > 0:
                    AddFont(descendants[0]);
                    break;
                // A Type3 glyph is a content stream drawn through the font's own resources.
                case "Type3" when Resolve(font.Get("Resources")) is PdfDictionary type3Resources:
                    WalkResources(new PdfResources(type3Resources, document));
                    break;
            }
        }

        void WalkResources(PdfResources? resources)
        {
            if (resources is null)
                return;
            if (resources.Dictionary.IsIndirect && !resourceSeen.Add(resources.Dictionary.ObjectNumber))
                return;

            if (resources.GetFonts() is { } fontDict)
                foreach (PdfObject font in fontDict.Values)
                    AddFont(font);

            if (resources.GetXObjects() is { } xobjects)
                foreach (PdfObject xobject in xobjects.Values)
                    WalkStreamResources(xobject);

            if (resources.GetPatterns() is { } patterns)
                foreach (PdfObject pattern in patterns.Values)
                    WalkStreamResources(pattern); // tiling patterns are streams that carry /Resources

            // An ExtGState /Font entry ([font size]) can be the only reference to a rendered font.
            if (resources.GetExtGStates() is { } extGStates)
                foreach (PdfObject graphicsState in extGStates.Values)
                    if (Resolve(graphicsState) is PdfDictionary gsDict
                        && Resolve(gsDict.Get("Font")) is PdfArray gsFont && gsFont.Count > 0)
                        AddFont(gsFont[0]);
        }

        void WalkStreamResources(PdfObject? streamObj)
        {
            if (Resolve(streamObj) is not PdfStream stream)
                return;
            if (stream.IsIndirect && !streamSeen.Add(stream.ObjectNumber))
                return;
            if (Resolve(stream.Dictionary.Get("Resources")) is PdfDictionary resourceDict)
                WalkResources(new PdfResources(resourceDict, document));
        }

        void WalkAppearance(PdfObject? apObj)
        {
            if (Resolve(apObj) is not PdfDictionary appearance)
                return;
            foreach (PdfObject state in appearance.Values) // /N, /D, /R
            {
                switch (Resolve(state))
                {
                    case PdfStream:
                        WalkStreamResources(state);
                        break;
                    case PdfDictionary subStates: // per-state appearances (e.g. button on/off)
                        foreach (PdfObject sub in subStates.Values)
                            WalkStreamResources(sub);
                        break;
                }
            }
        }

        // The nearest /Resources up a page's full /Parent chain (page.GetResources() only inherits one
        // level, unlike page.GetMediaBox()), so a font in a grandparent /Pages node is still reached.
        PdfResources? EffectiveResources(PdfDictionary? node)
        {
            var chainSeen = new HashSet<int>();
            while (node is not null)
            {
                if (node.IsIndirect && !chainSeen.Add(node.ObjectNumber))
                    break; // guard a cyclic /Parent chain
                if (Resolve(node.Get("Resources")) is PdfDictionary resourceDict)
                    return new PdfResources(resourceDict, document);
                node = Resolve(node.Get("Parent")) as PdfDictionary;
            }
            return null;
        }

        foreach (PdfPage page in pages)
            WalkResources(EffectiveResources(page.Dictionary));
        foreach (PdfDictionary annot in annotations)
            WalkAppearance(annot.Get("AP"));

        // AcroForm /DR fonts are rendered only when the viewer generates field appearances
        // (/NeedAppearances true); otherwise appearances come from /AP (already walked) and the /DR pool
        // is not necessarily drawn. Including /DR unconditionally would re-introduce the orphan over-report.
        if (catalog?.GetAcroForm() is { } acroForm
            && Resolve(acroForm.Get("NeedAppearances")) is PdfBoolean { Value: true }
            && Resolve(acroForm.Get("DR")) is PdfDictionary defaultResources)
        {
            WalkResources(new PdfResources(defaultResources, document));
        }

        return fonts;
    }
}
