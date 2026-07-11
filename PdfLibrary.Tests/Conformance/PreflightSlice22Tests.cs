using System.Linq;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 22 (PDF/UA-1) — the CP14 headings rule (<see cref="UaHeadingsRule"/>, ISO 14289-1:2014 7.4).
/// Three machine-checkable conditions calibrated against veraPDF's PDF_UA-1 profile: the numbered-heading
/// reading-order sequence (7.4.2), at most one &lt;H&gt; child per node (7.4.4), and no mixing of &lt;H&gt;
/// with numbered &lt;H#&gt; (7.4.4). The veraPDF PDF_UA-1 "7.4 Headings" corpus backs the red/green surface
/// (<see cref="CorpusOracleTests"/>); these are the hand-built edge cases.
/// </summary>
public class PreflightSlice22Tests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static ConformanceContext Ctx(PdfDocument doc) => new(doc, ConformanceProfile.PdfUA1);

    /// <summary>A structure element with type <paramref name="s"/> and the given /K children.</summary>
    private static PdfDictionary Elem(string s, params PdfObject[] kids) => new()
    {
        [N("Type")] = N("StructElem"),
        [N("S")] = N(s),
        [N("K")] = new PdfArray(kids),
    };

    /// <summary>A document with a structure tree whose root /K and element objects <paramref name="build"/>
    /// populates; the rule reads the tree through the catalog /StructTreeRoot.</summary>
    private static PdfDocument StructDoc(System.Action<PdfDocument, PdfDictionary> build)
    {
        var doc = new PdfDocument();
        var root = new PdfDictionary { [N("Type")] = N("StructTreeRoot") };
        build(doc, root);
        doc.AddObject(31, 0, root);
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("StructTreeRoot")] = Ref(31) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    private static Finding[] Run(PdfDocument doc) => new UaHeadingsRule().Check(Ctx(doc)).ToArray();

    // ── condition 1: numbered-heading sequence (7.4.2) ────────────────────────

    [Fact]
    public void H1_h2_h3_ascending_passes()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(52, 0, Elem("H3", new PdfInteger(2)));
            d.AddObject(51, 0, Elem("H2", new PdfInteger(1)));
            d.AddObject(50, 0, Elem("H1", new PdfInteger(0)));
            d.AddObject(40, 0, Elem("Document", Ref(50), Ref(51), Ref(52)));
            root[N("K")] = Ref(40);
        });
        Assert.Empty(Run(doc));
    }

    [Fact]
    public void Lone_h1_passes()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(50, 0, Elem("H1", new PdfInteger(0)));
            d.AddObject(40, 0, Elem("Document", Ref(50)));
            root[N("K")] = Ref(40);
        });
        Assert.Empty(Run(doc));
    }

    [Fact] // the 7.4.2-t01-fail-a shape: the first heading is H2, not H1
    public void First_heading_not_h1_is_flagged()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(52, 0, Elem("H4", new PdfInteger(2)));
            d.AddObject(51, 0, Elem("H3", new PdfInteger(1)));
            d.AddObject(50, 0, Elem("H2", new PdfInteger(0)));
            d.AddObject(40, 0, Elem("Document", Ref(50), Ref(51), Ref(52)));
            root[N("K")] = Ref(40);
        });
        Finding f = Assert.Single(Run(doc));
        Assert.Equal("ua-headings", f.RuleId);
        Assert.Contains("first heading is <H2>, not <H1>", f.Message);
        Assert.Equal(50, f.ObjectNumber);
        Assert.Null(f.PageIndex);
    }

    [Fact] // the 7.4.2-t01-fail-b shape: H1, H2, then a jump to H4 skips H3
    public void Ascending_skip_is_flagged()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(52, 0, Elem("H4", new PdfInteger(2)));
            d.AddObject(51, 0, Elem("H2", new PdfInteger(1)));
            d.AddObject(50, 0, Elem("H1", new PdfInteger(0)));
            d.AddObject(40, 0, Elem("Document", Ref(50), Ref(51), Ref(52)));
            root[N("K")] = Ref(40);
        });
        Finding f = Assert.Single(Run(doc));
        Assert.Equal("ua-headings", f.RuleId);
        Assert.Contains("Heading level jumps from H2 to H4, skipping H3", f.Message);
        Assert.Equal(52, f.ObjectNumber);
    }

    [Fact] // a jump across more than one level names the whole skipped range
    public void Ascending_skip_of_multiple_levels_names_the_range()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(51, 0, Elem("H4", new PdfInteger(1)));
            d.AddObject(50, 0, Elem("H1", new PdfInteger(0)));
            d.AddObject(40, 0, Elem("Document", Ref(50), Ref(51)));
            root[N("K")] = Ref(40);
        });
        Finding f = Assert.Single(Run(doc));
        Assert.Contains("Heading level jumps from H1 to H4, skipping H2–H3", f.Message);
    }

    [Fact] // the 7.4.2-t01-pass-c shape: repeats + descents + re-increment are all valid
    public void Repeats_descent_and_reincrement_pass()
    {
        var doc = StructDoc((d, root) =>
        {
            // H1, H2, H2, H1, H2 — a repeat (H2→H2), a descent (H2→H1), and a re-increment (H1→H2)
            d.AddObject(54, 0, Elem("H2", new PdfInteger(4)));
            d.AddObject(53, 0, Elem("H1", new PdfInteger(3)));
            d.AddObject(52, 0, Elem("H2", new PdfInteger(2)));
            d.AddObject(51, 0, Elem("H2", new PdfInteger(1)));
            d.AddObject(50, 0, Elem("H1", new PdfInteger(0)));
            d.AddObject(40, 0, Elem("Document", Ref(50), Ref(51), Ref(52), Ref(53), Ref(54)));
            root[N("K")] = Ref(40);
        });
        Assert.Empty(Run(doc));
    }

    [Fact] // unnumbered <H> is not part of the numbered sequence; a lone <H> raises no sequence finding
    public void Unnumbered_h_is_not_part_of_the_numbered_sequence()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(50, 0, Elem("H", new PdfInteger(0)));
            d.AddObject(40, 0, Elem("Document", Ref(50)));
            root[N("K")] = Ref(40);
        });
        Assert.Empty(Run(doc));
    }

    // ── condition 2: at most one <H> child per node (7.4.4) ───────────────────

    [Fact] // the 7.4.4-t01-pass-a shape: two sections each with a single <H> child
    public void One_h_child_per_node_passes()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(61, 0, Elem("H", new PdfInteger(1)));
            d.AddObject(51, 0, Elem("Sect", Ref(61)));
            d.AddObject(60, 0, Elem("H", new PdfInteger(0)));
            d.AddObject(50, 0, Elem("Sect", Ref(60)));
            d.AddObject(40, 0, Elem("Document", Ref(50), Ref(51)));
            root[N("K")] = Ref(40);
        });
        Assert.Empty(Run(doc));
    }

    [Fact] // the 7.4.4-t01-fail-a shape: a Sect with two <H> children
    public void Two_h_children_under_one_node_is_flagged()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(60, 0, Elem("H", new PdfInteger(0)));
            d.AddObject(61, 0, Elem("P", new PdfInteger(1)));
            d.AddObject(62, 0, Elem("H", new PdfInteger(2)));
            d.AddObject(50, 0, Elem("Sect", Ref(60), Ref(61), Ref(62)));
            d.AddObject(40, 0, Elem("Document", Ref(50)));
            root[N("K")] = Ref(40);
        });
        Finding f = Assert.Single(Run(doc));
        Assert.Equal("ua-headings", f.RuleId);
        Assert.Contains("more than one <H> heading", f.Message);
        Assert.Equal(50, f.ObjectNumber); // attributed to the parent Sect
    }

    // ── condition 3: no mixing of <H> and numbered <H#> (7.4.4) ───────────────

    [Fact]
    public void Only_unnumbered_h_passes()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(60, 0, Elem("H", new PdfInteger(0)));
            d.AddObject(50, 0, Elem("Sect", Ref(60)));
            d.AddObject(51, 0, Elem("H", new PdfInteger(1)));
            d.AddObject(40, 0, Elem("Document", Ref(50), Ref(51)));
            root[N("K")] = Ref(40);
        });
        Assert.Empty(Run(doc));
    }

    [Fact]
    public void Only_numbered_headings_pass()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(51, 0, Elem("H1", new PdfInteger(1)));
            d.AddObject(50, 0, Elem("H1", new PdfInteger(0)));
            d.AddObject(40, 0, Elem("Document", Ref(50), Ref(51)));
            root[N("K")] = Ref(40);
        });
        Assert.Empty(Run(doc));
    }

    [Fact] // the 7.4.4-t02 / -t03 shape: H and H1 together — exactly one document-level finding
    public void Mixing_h_and_numbered_headings_is_flagged_once()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(60, 0, Elem("H1", new PdfInteger(0)));
            d.AddObject(50, 0, Elem("Sect", Ref(60)));
            d.AddObject(61, 0, Elem("H", new PdfInteger(1)));
            d.AddObject(51, 0, Elem("Sect", Ref(61)));
            d.AddObject(40, 0, Elem("Document", Ref(50), Ref(51)));
            root[N("K")] = Ref(40);
        });
        Finding f = Assert.Single(Run(doc), x => x.Message.Contains("both <H> and numbered"));
        Assert.Equal("ua-headings", f.RuleId);
        Assert.Null(f.ObjectNumber);
        Assert.Null(f.PageIndex);
    }

    // ── non-tagged document ───────────────────────────────────────────────────

    [Fact]
    public void Untagged_document_produces_no_findings()
    {
        var doc = new PdfDocument();
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog") }); // no /StructTreeRoot
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        Assert.Empty(Run(doc));
    }
}
