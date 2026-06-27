using PdfLibrary.Builder;
using PdfLibrary.Builder.Bookmark;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

/// <summary>
/// Tests for OutlineTree.Build / Rewire, including the /Count sign fix
/// (closed nodes get negative /Count) and the DestinationRepairer refactor
/// regression (prune-on-page-delete must still work).
/// </summary>
public class OutlineTreeTests
{
    private static PdfObject? Resolve(PdfDocument doc, PdfObject? obj) =>
        obj is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : obj;

    // ── Build helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates a document with an outline tree:
    ///   A (open)
    ///     A1
    ///     A2
    ///   B (closed)
    ///     B1
    /// Returns the doc loaded+materialized, plus the /First ref of /Outlines.
    /// </summary>
    private static (PdfDocument doc, PdfObject firstRef) DocWithOutline()
    {
        // A (open)
        //   A1
        //   A2
        // B (closed / collapsed)
        //   B1
        byte[] bytes = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("P0", 100, 700))
            .AddPage(p => p.AddText("P1", 100, 700))
            .AddPage(p => p.AddText("P2", 100, 700))
            .AddBookmark("A", b =>
            {
                b.ToPage(0);
                b.AddChild("A1", childB => childB.ToPage(0));
                b.AddChild("A2", childB => childB.ToPage(1));
            })
            .AddBookmark("B", b =>
            {
                b.ToPage(2).Collapsed();
                b.AddChild("B1", childB => childB.ToPage(2));
            })
            .ToByteArray();

        PdfDocument doc = PdfDocument.Load(new MemoryStream(bytes));
        doc.Edit(); // materialize + flatten

        PdfDictionary? outlines = Resolve(doc, doc.CatalogDictionary?.Get(new PdfName("Outlines"))) as PdfDictionary;
        PdfObject firstRef = outlines!.Get(new PdfName("First"))!;
        return (doc, firstRef);
    }

    // ── Build tests ────────────────────────────────────────────────────────

    [Fact]
    public void Build_ReadsTopLevelNodes()
    {
        (PdfDocument doc, PdfObject firstRef) = DocWithOutline();
        List<OutlineNode> nodes = OutlineTree.Build(doc, firstRef);
        Assert.Equal(2, nodes.Count);
        Assert.Equal("A", ((PdfString)nodes[0].Dict[new PdfName("Title")]).Value);
        Assert.Equal("B", ((PdfString)nodes[1].Dict[new PdfName("Title")]).Value);
        doc.Dispose();
    }

    [Fact]
    public void Build_ReadsChildren()
    {
        (PdfDocument doc, PdfObject firstRef) = DocWithOutline();
        List<OutlineNode> nodes = OutlineTree.Build(doc, firstRef);
        Assert.Equal(2, nodes[0].Children.Count); // A has A1 and A2
        Assert.Single(nodes[1].Children); // B has B1
        doc.Dispose();
    }

    [Fact]
    public void Build_IsOpen_TrueForPositiveCount()
    {
        (PdfDocument doc, PdfObject firstRef) = DocWithOutline();
        List<OutlineNode> nodes = OutlineTree.Build(doc, firstRef);
        // Node A is open (positive /Count or missing /Count means open)
        Assert.True(nodes[0].IsOpen);
        doc.Dispose();
    }

    [Fact]
    public void Build_IsOpen_FalseForNegativeCount()
    {
        // Build a minimal doc, then manually set a negative /Count on a node
        // to simulate a closed outline item
        (PdfDocument doc, PdfObject firstRef) = DocWithOutline();
        List<OutlineNode> nodes = OutlineTree.Build(doc, firstRef);
        // node B was created with IsOpen=false. The writer should have written negative /Count.
        // Verify Build reads it as closed.
        Assert.False(nodes[1].IsOpen);
        doc.Dispose();
    }

    // ── Rewire tests ────────────────────────────────────────────────────────

    [Fact]
    public void Rewire_SetsFirstAndLast()
    {
        (PdfDocument doc, PdfObject firstRef) = DocWithOutline();
        List<OutlineNode> nodes = OutlineTree.Build(doc, firstRef);

        // Create a fake parent ref for testing
        var parentRef = new PdfIndirectReference(99, 0);
        (PdfObject? first, PdfObject? last, int count) = OutlineTree.Rewire(parentRef, nodes);

        Assert.Equal(nodes[0].Ref, first);
        Assert.Equal(nodes[1].Ref, last);
        doc.Dispose();
    }

    [Fact]
    public void Rewire_SetsPrevNext()
    {
        (PdfDocument doc, PdfObject firstRef) = DocWithOutline();
        List<OutlineNode> nodes = OutlineTree.Build(doc, firstRef);

        var parentRef = new PdfIndirectReference(99, 0);
        OutlineTree.Rewire(parentRef, nodes);

        // A.Prev should be absent, A.Next should be B's ref
        Assert.False(nodes[0].Dict.TryGetValue(new PdfName("Prev"), out _));
        Assert.True(nodes[0].Dict.TryGetValue(new PdfName("Next"), out PdfObject nextOfA));
        Assert.Equal(nodes[1].Ref, nextOfA);

        // B.Prev should be A's ref, B.Next should be absent
        Assert.True(nodes[1].Dict.TryGetValue(new PdfName("Prev"), out PdfObject prevOfB));
        Assert.Equal(nodes[0].Ref, prevOfB);
        Assert.False(nodes[1].Dict.TryGetValue(new PdfName("Next"), out _));
        doc.Dispose();
    }

    [Fact]
    public void Rewire_OpenNode_WritesPositiveCount()
    {
        (PdfDocument doc, PdfObject firstRef) = DocWithOutline();
        List<OutlineNode> nodes = OutlineTree.Build(doc, firstRef);

        var parentRef = new PdfIndirectReference(99, 0);
        OutlineTree.Rewire(parentRef, nodes);

        // Node A is open and has 2 children → /Count must be positive
        Assert.True(nodes[0].Dict.TryGetValue(new PdfName("Count"), out PdfObject countA));
        Assert.IsType<PdfInteger>(countA);
        Assert.True(((PdfInteger)countA).Value > 0);
        doc.Dispose();
    }

    [Fact]
    public void Rewire_ClosedNode_WritesNegativeCount()
    {
        (PdfDocument doc, PdfObject firstRef) = DocWithOutline();
        List<OutlineNode> nodes = OutlineTree.Build(doc, firstRef);

        var parentRef = new PdfIndirectReference(99, 0);
        OutlineTree.Rewire(parentRef, nodes);

        // Node B is closed and has 1 child → /Count must be NEGATIVE per ISO 32000 §12.3.3
        Assert.True(nodes[1].Dict.TryGetValue(new PdfName("Count"), out PdfObject countB));
        Assert.IsType<PdfInteger>(countB);
        Assert.True(((PdfInteger)countB).Value < 0);
        doc.Dispose();
    }

    [Fact]
    public void Rewire_EmptyNode_RemovesCount()
    {
        // Build a fresh single-node outline with no children
        byte[] bytes = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("P0", 100, 700))
            .AddBookmark("Solo", 0)
            .ToByteArray();
        PdfDocument doc = PdfDocument.Load(new MemoryStream(bytes));
        doc.Edit();
        PdfDictionary? outlines = Resolve(doc, doc.CatalogDictionary?.Get(new PdfName("Outlines"))) as PdfDictionary;
        PdfObject firstRef = outlines!.Get(new PdfName("First"))!;

        List<OutlineNode> nodes = OutlineTree.Build(doc, firstRef);
        Assert.Single(nodes);

        var parentRef = new PdfIndirectReference(99, 0);
        OutlineTree.Rewire(parentRef, nodes);

        // Leaf node → /Count must be absent
        Assert.False(nodes[0].Dict.TryGetValue(new PdfName("Count"), out _));
        doc.Dispose();
    }

    [Fact]
    public void Rewire_VisibleCount_ExcludesClosedSubtree()
    {
        (PdfDocument doc, PdfObject firstRef) = DocWithOutline();
        List<OutlineNode> nodes = OutlineTree.Build(doc, firstRef);

        // The outline root /Count should only count OPEN descendants.
        // A is open with 2 children (A1, A2), B is closed with 1 child (B1).
        // Total visible = A + A1 + A2 + B = 4 (B1 is hidden by B's closed state)
        var parentRef = new PdfIndirectReference(99, 0);
        (_, _, int totalCount) = OutlineTree.Rewire(parentRef, nodes);
        Assert.Equal(4, totalCount); // A, A1, A2, B (B1 excluded)
        doc.Dispose();
    }

    [Fact]
    public void BuildRewire_Idempotent()
    {
        (PdfDocument doc, PdfObject firstRef) = DocWithOutline();
        var parentRef = new PdfIndirectReference(99, 0);

        // First pass
        List<OutlineNode> nodes1 = OutlineTree.Build(doc, firstRef);
        OutlineTree.Rewire(parentRef, nodes1);

        // Second pass on the same in-memory tree
        List<OutlineNode> nodes2 = OutlineTree.Build(doc, firstRef);
        (_, _, int count2) = OutlineTree.Rewire(parentRef, nodes2);

        Assert.Equal(nodes1.Count, nodes2.Count);
        Assert.Equal(4, count2); // same visible count
        doc.Dispose();
    }

    // ── DestinationRepairer regression ─────────────────────────────────────

    [Fact]
    public void DestinationRepairer_StillPrunesOnPageDelete_AfterRefactor()
    {
        byte[] bytes = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("PAGE A", 100, 700))
            .AddPage(p => p.AddText("PAGE B", 100, 700))
            .AddBookmark("Bookmark A", 0)
            .AddBookmark("Bookmark B", 1)
            .ToByteArray();

        using PdfDocument doc = PdfDocument.Load(new MemoryStream(bytes));
        PdfDocumentEditor edit = doc.Edit();
        edit.Pages.RemoveAt(1);

        // After deletion of page 1, Bookmark B (which targets page 1) must be removed
        var titles = new List<string>();
        if (Resolve(doc, doc.CatalogDictionary?.Get(new PdfName("Outlines"))) is PdfDictionary outlines)
        {
            Walk(outlines.Get(new PdfName("First")));
        }
        Assert.Contains("Bookmark A", titles);
        Assert.DoesNotContain("Bookmark B", titles);

        void Walk(PdfObject? reference)
        {
            var guard = 0;
            while (reference is not null && guard++ < 10000)
            {
                if (Resolve(doc, reference) is not PdfDictionary item) break;
                if (item.Get(new PdfName("Title")) is PdfString t) titles.Add(t.Value);
                Walk(item.Get(new PdfName("First")));
                reference = item.Get(new PdfName("Next"));
            }
        }
    }

    [Fact]
    public void DestinationRepairer_ClosedNodeChildren_PromotedOnPrune()
    {
        // B is closed and targets the deleted page; its child B1 should be promoted
        byte[] bytes = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("P0", 100, 700))
            .AddPage(p => p.AddText("P1", 100, 700))
            .AddBookmark("B", b =>
            {
                b.ToPage(1).Collapsed();
                b.AddChild("B1", childB => childB.ToPage(0));
            })
            .ToByteArray();

        using PdfDocument doc = PdfDocument.Load(new MemoryStream(bytes));
        PdfDocumentEditor edit = doc.Edit();
        edit.Pages.RemoveAt(1); // removes page 1 — B targets it, B1 targets page 0

        var titles = new List<string>();
        if (Resolve(doc, doc.CatalogDictionary?.Get(new PdfName("Outlines"))) is PdfDictionary outlines)
        {
            Walk(outlines.Get(new PdfName("First")));
        }
        // B is pruned; B1 is promoted to top level
        Assert.DoesNotContain("B", titles);
        Assert.Contains("B1", titles);

        void Walk(PdfObject? reference)
        {
            var guard = 0;
            while (reference is not null && guard++ < 10000)
            {
                if (Resolve(doc, reference) is not PdfDictionary item) break;
                if (item.Get(new PdfName("Title")) is PdfString t) titles.Add(t.Value);
                Walk(item.Get(new PdfName("First")));
                reference = item.Get(new PdfName("Next"));
            }
        }
    }
}
