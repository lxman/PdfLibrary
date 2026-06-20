using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing;

/// <summary>
/// A single node in an outline (bookmark) tree, holding its PDF dictionary,
/// its indirect reference, its children, and its open/closed state.
/// </summary>
internal sealed class OutlineNode
{
    public PdfDictionary Dict = null!;
    public PdfObject Ref = null!;
    public List<OutlineNode> Children = [];
    /// <summary>
    /// True if the node is open (children visible); false if collapsed.
    /// Derived from the sign of /Count on Build; written back on Rewire.
    /// </summary>
    public bool IsOpen = true;
}

/// <summary>
/// Reusable outline-tree helpers: Build reads the tree from PDF dictionaries;
/// Rewire fixes /First /Last /Prev /Next /Parent /Count after any mutation.
///
/// /Count sign rule (ISO 32000 §12.3.3):
///   • open node  → /Count = positive visible-descendant count
///   • closed node → /Count = negative direct-child count
///   • leaf node   → /Count absent
///
/// Closed subtrees are excluded from ancestor visible-descendant counts.
/// </summary>
internal static class OutlineTree
{
    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a list of top-level outline nodes by walking the /First→/Next chain
    /// starting from <paramref name="firstRef"/>, recursing into /First chains for children.
    /// IsOpen is derived from the sign of /Count (absent or positive ⇒ open; negative ⇒ closed).
    /// </summary>
    public static List<OutlineNode> Build(PdfDocument doc, PdfObject? firstRef)
    {
        var list = new List<OutlineNode>();
        PdfObject? cur = firstRef;
        var guard = 0;
        while (cur is not null && guard++ < 100_000)
        {
            if (Resolve(doc, cur) is not PdfDictionary dict) break;
            bool isOpen = ReadIsOpen(dict);
            var node = new OutlineNode
            {
                Dict = dict,
                Ref  = cur,
                Children = Build(doc, dict.Get(new PdfName("First"))),
                IsOpen = isOpen
            };
            list.Add(node);
            cur = dict.Get(new PdfName("Next"));
        }
        return list;
    }

    /// <summary>
    /// Rewires /First /Last /Prev /Next /Parent /Count for all nodes in <paramref name="children"/>
    /// (and recursively their children) under <paramref name="parentRef"/>.
    /// Returns (first ref, last ref, visible-descendant count for the parent's /Count).
    /// The visible-descendant count follows ISO 32000 §12.3.3: closed subtrees are not counted.
    /// </summary>
    public static (PdfObject? first, PdfObject? last, int count) Rewire(
        PdfObject parentRef, List<OutlineNode> children)
    {
        var visibleTotal = 0;
        for (var i = 0; i < children.Count; i++)
        {
            OutlineNode node = children[i];

            // Fix /Parent, /Prev, /Next
            node.Dict[new PdfName("Parent")] = parentRef;
            SetOrRemove(node.Dict, "Prev", i > 0 ? children[i - 1].Ref : null);
            SetOrRemove(node.Dict, "Next", i + 1 < children.Count ? children[i + 1].Ref : null);

            // Recurse into children
            (PdfObject? cFirst, PdfObject? cLast, int cVisibleCount) = Rewire(node.Ref, node.Children);
            SetOrRemove(node.Dict, "First", cFirst);
            SetOrRemove(node.Dict, "Last",  cLast);

            if (node.Children.Count > 0)
            {
                if (node.IsOpen)
                {
                    // Open: positive visible-descendant count (direct children + their open subtrees)
                    node.Dict[new PdfName("Count")] = new PdfInteger(cVisibleCount);
                }
                else
                {
                    // Closed: negative direct-child count (ISO 32000 §12.3.3)
                    node.Dict[new PdfName("Count")] = new PdfInteger(-node.Children.Count);
                }
            }
            else
            {
                node.Dict.Remove(new PdfName("Count"));
            }

            // Contribute to parent's visible count:
            //   • always +1 for this node itself
            //   • +cVisibleCount only if this node is OPEN (closed hides its subtree)
            visibleTotal += 1;
            if (node.IsOpen) visibleTotal += cVisibleCount;
        }

        return (
            children.Count > 0 ? children[0].Ref : null,
            children.Count > 0 ? children[^1].Ref : null,
            visibleTotal);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static PdfObject? Resolve(PdfDocument doc, PdfObject? obj) =>
        obj is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : obj;

    private static void SetOrRemove(PdfDictionary dict, string key, PdfObject? value)
    {
        if (value is null) dict.Remove(new PdfName(key));
        else dict[new PdfName(key)] = value;
    }

    /// <summary>
    /// Reads the IsOpen flag from the /Count entry sign.
    /// Absent or positive /Count → open; negative /Count → closed.
    /// </summary>
    private static bool ReadIsOpen(PdfDictionary dict)
    {
        if (!dict.TryGetValue(new PdfName("Count"), out PdfObject countObj))
            return true; // absent = open (leaf or open root)
        return countObj is not PdfInteger { Value: < 0 };
    }
}
