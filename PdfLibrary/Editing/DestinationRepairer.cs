using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing;

/// <summary>
/// After a page is deleted, strips navigation that resolves to it: outline (bookmark) items
/// (promoting their surviving children), named destinations, and link annotations on other pages.
/// Only delete needs this — reorder/insert keep the page object, so reference-based destinations stay valid.
/// </summary>
internal static class DestinationRepairer
{
    public static void OnPageRemoved(PdfDocument doc, PdfDictionary removedPage)
    {
        int pageObjNum = removedPage.ObjectNumber;
        RepairOutlines(doc, pageObjNum);
        RepairNamedDestinations(doc, pageObjNum);
        RepairLinkAnnotations(doc, pageObjNum);
    }

    private static PdfObject? Resolve(PdfDocument doc, PdfObject? obj) =>
        obj is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : obj;

    private static PdfObject? GetDest(PdfDocument doc, PdfDictionary holder)
    {
        if (holder.TryGetValue(new PdfName("Dest"), out PdfObject d)) return d;
        if (holder.TryGetValue(new PdfName("A"), out PdfObject a) && Resolve(doc, a) is PdfDictionary action
            && action.TryGetValue(new PdfName("S"), out PdfObject s) && s is PdfName { Value: "GoTo" }
            && action.TryGetValue(new PdfName("D"), out PdfObject dd))
            return dd;
        return null;
    }

    private static bool TargetsPage(PdfDocument doc, PdfDictionary holder, int pageObjNum)
    {
        PdfObject? dest = GetDest(doc, holder);
        return dest is not null && DestTargetsPage(doc, dest, pageObjNum, 0);
    }

    private static bool DestTargetsPage(PdfDocument doc, PdfObject dest, int pageObjNum, int depth)
    {
        if (depth > 8) return false;
        switch (Resolve(doc, dest))
        {
            case PdfArray { Count: > 0 } arr:
                return arr[0] is PdfIndirectReference r && r.ObjectNumber == pageObjNum;
            case PdfString s:
            {
                PdfObject? nd = LookupNamedDest(doc, s.Value);
                return nd is not null && DestTargetsPage(doc, nd, pageObjNum, depth + 1);
            }
            case PdfName n:
            {
                PdfObject? nd = LookupNamedDest(doc, n.Value);
                return nd is not null && DestTargetsPage(doc, nd, pageObjNum, depth + 1);
            }
            case PdfDictionary dict when dict.TryGetValue(new PdfName("D"), out PdfObject inner):
                return DestTargetsPage(doc, inner, pageObjNum, depth + 1);
            default:
                return false;
        }
    }

    internal static PdfObject? LookupNamedDest(PdfDocument doc, string name)
    {
        PdfDictionary? catalog = doc.CatalogDictionary;
        if (catalog is null) return null;
        if (Resolve(doc, catalog.Get(new PdfName("Dests"))) is PdfDictionary legacy
            && legacy.TryGetValue(new PdfName(name), out PdfObject hit))
            return hit;
        if (Resolve(doc, catalog.Get(new PdfName("Names"))) is PdfDictionary names
            && Resolve(doc, names.Get(new PdfName("Dests"))) is PdfDictionary tree)
            return NameTreeLookup(doc, tree, name);
        return null;
    }

    internal static PdfObject? NameTreeLookup(PdfDocument doc, PdfDictionary node, string name)
    {
        if (Resolve(doc, node.Get(new PdfName("Names"))) is PdfArray names)
            for (var i = 0; i + 1 < names.Count; i += 2)
                if (names[i] is PdfString key && key.Value == name)
                    return names[i + 1];
        if (Resolve(doc, node.Get(new PdfName("Kids"))) is not PdfArray kids) return null;
        foreach (PdfObject kid in kids)
            if (Resolve(doc, kid) is PdfDictionary child)
            {
                PdfObject? found = NameTreeLookup(doc, child, name);
                if (found is not null) return found;
            }
        return null;
    }

    private static void SetOrRemove(PdfDictionary dict, string key, PdfObject? value)
    {
        if (value is null) dict.Remove(new PdfName(key));
        else dict[new PdfName(key)] = value;
    }

    private static void RepairOutlines(PdfDocument doc, int pageObjNum)
    {
        PdfObject? outlinesRef = doc.CatalogDictionary?.Get(new PdfName("Outlines"));
        if (outlinesRef is null || Resolve(doc, outlinesRef) is not PdfDictionary outlines) return;

        List<OutlineNode> top = OutlineTree.Build(doc, outlines.Get(new PdfName("First")));
        List<OutlineNode> pruned = Prune(doc, top, pageObjNum);
        (PdfObject? first, PdfObject? last, int count) = OutlineTree.Rewire(outlinesRef, pruned);
        SetOrRemove(outlines, "First", first);
        SetOrRemove(outlines, "Last", last);
        outlines[new PdfName("Count")] = new PdfInteger(count);
    }

    private static List<OutlineNode> Prune(PdfDocument doc, List<OutlineNode> nodes, int pageObjNum)
    {
        var result = new List<OutlineNode>();
        foreach (OutlineNode node in nodes)
        {
            List<OutlineNode> prunedChildren = Prune(doc, node.Children, pageObjNum);
            if (TargetsPage(doc, node.Dict, pageObjNum))
                result.AddRange(prunedChildren);
            else
            {
                node.Children = prunedChildren;
                result.Add(node);
            }
        }
        return result;
    }

    private static void RepairNamedDestinations(PdfDocument doc, int pageObjNum)
    {
        PdfDictionary? catalog = doc.CatalogDictionary;
        if (catalog is null) return;

        if (Resolve(doc, catalog.Get(new PdfName("Dests"))) is PdfDictionary legacy)
        {
            List<PdfName> kill = (from kv in legacy where DestTargetsPage(doc, kv.Value, pageObjNum, 0) select kv.Key).ToList();
            foreach (PdfName k in kill) legacy.Remove(k);
        }

        if (Resolve(doc, catalog.Get(new PdfName("Names"))) is PdfDictionary names
            && Resolve(doc, names.Get(new PdfName("Dests"))) is PdfDictionary tree)
            PruneNameTree(doc, tree, pageObjNum);
    }

    private static void PruneNameTree(PdfDocument doc, PdfDictionary node, int pageObjNum)
    {
        if (Resolve(doc, node.Get(new PdfName("Names"))) is PdfArray names)
            for (int i = names.Count - 2; i >= 0; i -= 2)
                if (DestTargetsPage(doc, names[i + 1], pageObjNum, 0))
                {
                    names.RemoveAt(i + 1);
                    names.RemoveAt(i);
                }

        if (Resolve(doc, node.Get(new PdfName("Kids"))) is not PdfArray kids) return;
        foreach (PdfObject kid in kids)
            if (Resolve(doc, kid) is PdfDictionary child)
                PruneNameTree(doc, child, pageObjNum);
    }

    private static void RepairLinkAnnotations(PdfDocument doc, int pageObjNum)
    {
        foreach (PdfDictionary page in PageTreeOps.PageDicts(doc))
        {
            if (Resolve(doc, page.Get(new PdfName("Annots"))) is not PdfArray annots) continue;
            for (int i = annots.Count - 1; i >= 0; i--)
            {
                if (Resolve(doc, annots[i]) is not PdfDictionary annot) continue;
                bool isLink = annot.TryGetValue(PdfName.Subtype, out PdfObject st) && st is PdfName { Value: "Link" };
                if (isLink && TargetsPage(doc, annot, pageObjNum))
                    annots.RemoveAt(i);
            }
        }
    }
}
