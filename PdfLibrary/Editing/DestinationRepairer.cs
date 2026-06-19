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
            case PdfArray arr when arr.Count > 0:
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

    private static PdfObject? LookupNamedDest(PdfDocument doc, string name)
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

    private static PdfObject? NameTreeLookup(PdfDocument doc, PdfDictionary node, string name)
    {
        if (Resolve(doc, node.Get(new PdfName("Names"))) is PdfArray names)
            for (var i = 0; i + 1 < names.Count; i += 2)
                if (names[i] is PdfString key && key.Value == name)
                    return names[i + 1];
        if (Resolve(doc, node.Get(new PdfName("Kids"))) is PdfArray kids)
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

    private sealed class OutlineNode
    {
        public PdfDictionary Dict = null!;
        public PdfObject Ref = null!;
        public List<OutlineNode> Children = [];
    }

    private static void RepairOutlines(PdfDocument doc, int pageObjNum)
    {
        PdfObject? outlinesRef = doc.CatalogDictionary?.Get(new PdfName("Outlines"));
        if (outlinesRef is null || Resolve(doc, outlinesRef) is not PdfDictionary outlines) return;

        List<OutlineNode> top = BuildChildren(doc, outlines.Get(new PdfName("First")));
        List<OutlineNode> pruned = Prune(doc, top, pageObjNum);
        (PdfObject? first, PdfObject? last, int count) = Rewire(outlinesRef, pruned);
        SetOrRemove(outlines, "First", first);
        SetOrRemove(outlines, "Last", last);
        outlines[new PdfName("Count")] = new PdfInteger(count);
    }

    private static List<OutlineNode> BuildChildren(PdfDocument doc, PdfObject? firstRef)
    {
        var list = new List<OutlineNode>();
        PdfObject? cur = firstRef;
        var guard = 0;
        while (cur is not null && guard++ < 100000)
        {
            if (Resolve(doc, cur) is not PdfDictionary dict) break;
            var node = new OutlineNode { Dict = dict, Ref = cur, Children = BuildChildren(doc, dict.Get(new PdfName("First"))) };
            list.Add(node);
            cur = dict.Get(new PdfName("Next"));
        }
        return list;
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

    private static (PdfObject? first, PdfObject? last, int count) Rewire(PdfObject parentRef, List<OutlineNode> children)
    {
        var total = 0;
        for (var i = 0; i < children.Count; i++)
        {
            OutlineNode node = children[i];
            node.Dict[new PdfName("Parent")] = parentRef;
            SetOrRemove(node.Dict, "Prev", i > 0 ? children[i - 1].Ref : null);
            SetOrRemove(node.Dict, "Next", i + 1 < children.Count ? children[i + 1].Ref : null);

            (PdfObject? cFirst, PdfObject? cLast, int cCount) = Rewire(node.Ref, node.Children);
            SetOrRemove(node.Dict, "First", cFirst);
            SetOrRemove(node.Dict, "Last", cLast);
            if (node.Children.Count > 0) node.Dict[new PdfName("Count")] = new PdfInteger(cCount);
            else node.Dict.Remove(new PdfName("Count"));
            total += 1 + cCount;
        }
        return (children.Count > 0 ? children[0].Ref : null,
                children.Count > 0 ? children[^1].Ref : null, total);
    }

    private static void RepairNamedDestinations(PdfDocument doc, int pageObjNum)
    {
        PdfDictionary? catalog = doc.CatalogDictionary;
        if (catalog is null) return;

        if (Resolve(doc, catalog.Get(new PdfName("Dests"))) is PdfDictionary legacy)
        {
            var kill = new List<PdfName>();
            foreach (KeyValuePair<PdfName, PdfObject> kv in legacy)
                if (DestTargetsPage(doc, kv.Value, pageObjNum, 0)) kill.Add(kv.Key);
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
        if (Resolve(doc, node.Get(new PdfName("Kids"))) is PdfArray kids)
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
