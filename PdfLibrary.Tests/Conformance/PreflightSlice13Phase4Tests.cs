using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 13 (PDF/UA-1) phase 4 — marked-content completeness (ISO 14289-1, 7.1) and the figure-alt
/// content-stream extension (7.3). Covers <see cref="UaContentTaggedRule"/> (real content must be tagged or an
/// artifact), <see cref="UaArtifactNestingRule"/> (artifacts and tagged content must not nest), and the second
/// case of <see cref="UaFigureAltRule"/> (a figure with neither /Alt nor /ActualText whose marked-content
/// sequence also supplies no /ActualText). The veraPDF PDF_UA-1 corpus backs the red/green surface
/// (<see cref="CorpusOracleTests"/>); these are the hand-built edge cases.
/// </summary>
public class PreflightSlice13Phase4Tests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfString Str(string s) => new(Encoding.ASCII.GetBytes(s));

    private static PdfArray Box() =>
        new(new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792));

    private static ConformanceContext Ctx(PdfDocument doc) => new(doc, ConformanceProfile.PdfUA1);

    /// <summary>A one-page document whose single content stream is <paramref name="content"/>. No structure
    /// tree — enough for the marked-content rules, which read only page content.</summary>
    private static PdfDocument DocWithContent(string content)
    {
        var doc = new PdfDocument();
        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(content)));
        doc.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("Contents")] = Ref(4),
            [N("Resources")] = new PdfDictionary(),
            [N("MediaBox")] = Box(),
        });
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(Ref(3)),
            [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    /// <summary>A one-page document with a structure tree whose sole element is <paramref name="figure"/>, and
    /// a page carrying <paramref name="content"/>. Lets the figure-alt rule reconcile the element against the
    /// content-stream /ActualText its MCID reaches.</summary>
    private static PdfDocument DocFigureWithContent(PdfDictionary figure, string content)
    {
        var doc = new PdfDocument();
        doc.AddObject(50, 0, figure);
        doc.AddObject(31, 0, new PdfDictionary { [N("Type")] = N("StructTreeRoot"), [N("K")] = Ref(50) });
        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(content)));
        doc.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("Contents")] = Ref(4),
            [N("Resources")] = new PdfDictionary(),
            [N("MediaBox")] = Box(),
        });
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(Ref(3)),
            [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary
        {
            [N("Type")] = N("Catalog"),
            [N("Pages")] = Ref(2),
            [N("StructTreeRoot")] = Ref(31),
        });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    /// <summary>A minimal document whose StructTreeRoot carries the given <paramref name="roleMap"/> (or none
    /// when null) — enough for the RoleMap rule, which reads only StructTreeRoot /RoleMap.</summary>
    private static PdfDocument DocWithRoleMap(PdfDictionary? roleMap)
    {
        var doc = new PdfDocument();
        var root = new PdfDictionary { [N("Type")] = N("StructTreeRoot") };
        if (roleMap is not null) root[N("RoleMap")] = roleMap;
        doc.AddObject(31, 0, root);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(3, 0, new PdfDictionary { [N("Type")] = N("Page"), [N("Parent")] = Ref(2) });
        doc.AddObject(1, 0, new PdfDictionary
        {
            [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2), [N("StructTreeRoot")] = Ref(31),
        });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    // ── ua-role-map (7.1 t6/t7) ───────────────────────────────────────────────

    [Fact]
    public void Circular_role_mapping_is_flagged()
    {
        // Custom → Custom2 → Custom (a cycle that never reaches a standard type).
        var roleMap = new PdfDictionary
        {
            [N("Custom")] = N("Custom2"),
            [N("Custom2")] = N("Custom"),
        };
        Finding f = Assert.Single(new UaRoleMapRule().Check(Ctx(DocWithRoleMap(roleMap))));
        Assert.Equal("ua-role-map", f.RuleId);
        Assert.Contains("circular", f.Message);
    }

    [Fact]
    public void Self_referential_role_mapping_is_flagged()
    {
        var roleMap = new PdfDictionary { [N("Custom")] = N("Custom") };
        Finding f = Assert.Single(new UaRoleMapRule().Check(Ctx(DocWithRoleMap(roleMap))));
        Assert.Contains("circular", f.Message);
    }

    [Fact]
    public void Remapping_a_standard_type_is_flagged()
    {
        // "P" is a standard structure type; using it as a RoleMap key remaps it — forbidden.
        var roleMap = new PdfDictionary { [N("P")] = N("Span") };
        Finding f = Assert.Single(new UaRoleMapRule().Check(Ctx(DocWithRoleMap(roleMap))));
        Assert.Equal("ua-role-map", f.RuleId);
        Assert.Contains("remapped", f.Message);
        Assert.Contains("'P'", f.Message);
    }

    [Fact]
    public void Custom_type_mapped_to_a_standard_type_passes()
    {
        // FP-safety: the normal, conformant shape — a custom type role-mapped to a standard one.
        var roleMap = new PdfDictionary { [N("Heading1")] = N("H1"), [N("BodyText")] = N("P") };
        Assert.Empty(new UaRoleMapRule().Check(Ctx(DocWithRoleMap(roleMap))));
    }

    [Fact]
    public void Absent_role_map_passes()
    {
        // FP-safety: no /RoleMap at all (common — a document using only standard types) is conformant.
        Assert.Empty(new UaRoleMapRule().Check(Ctx(DocWithRoleMap(null))));
    }

    [Fact]
    public void Long_terminating_chain_passes()
    {
        // FP-safety: a multi-hop chain that ends at a standard type is valid — must not be seen as a cycle.
        var roleMap = new PdfDictionary
        {
            [N("Custom")] = N("Custom2"), [N("Custom2")] = N("Custom3"), [N("Custom3")] = N("H1"),
        };
        Assert.Empty(new UaRoleMapRule().Check(Ctx(DocWithRoleMap(roleMap))));
    }

    [Fact]
    public void Diamond_role_mapping_passes()
    {
        // FP-safety: a shared-tail DAG (A→C, B→C) is acyclic and must not be flagged.
        var roleMap = new PdfDictionary
        {
            [N("A")] = N("C"), [N("B")] = N("C"), [N("C")] = N("Div"),
        };
        Assert.Empty(new UaRoleMapRule().Check(Ctx(DocWithRoleMap(roleMap))));
    }

    [Fact]
    public void Tail_only_cycle_is_flagged()
    {
        // A→B→C→B: the cycle is reachable only via the A tail; still detected.
        var roleMap = new PdfDictionary
        {
            [N("A")] = N("B"), [N("B")] = N("C"), [N("C")] = N("B"),
        };
        Finding f = Assert.Single(new UaRoleMapRule().Check(Ctx(DocWithRoleMap(roleMap))));
        Assert.Contains("circular", f.Message);
    }

    [Fact]
    public void Non_name_role_map_value_is_ignored()
    {
        // FP-safety: an entry whose value is not a name is dropped, not crashed or spuriously flagged.
        var roleMap = new PdfDictionary { [N("Custom")] = new PdfInteger(5) };
        Assert.Empty(new UaRoleMapRule().Check(Ctx(DocWithRoleMap(roleMap))));
    }

    [Fact]
    public void Non_dictionary_role_map_is_ignored()
    {
        // FP-safety: a malformed /RoleMap that is not a dictionary yields no finding.
        var doc = new PdfDocument();
        var root = new PdfDictionary { [N("Type")] = N("StructTreeRoot"), [N("RoleMap")] = N("Bogus") };
        doc.AddObject(31, 0, root);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(3, 0, new PdfDictionary { [N("Type")] = N("Page"), [N("Parent")] = Ref(2) });
        doc.AddObject(1, 0, new PdfDictionary
        {
            [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2), [N("StructTreeRoot")] = Ref(31),
        });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        Assert.Empty(new UaRoleMapRule().Check(Ctx(doc)));
    }

    // ── ua-content-tagged (7.1) ───────────────────────────────────────────────

    [Fact]
    public void Untagged_text_is_flagged()
    {
        Finding f = Assert.Single(new UaContentTaggedRule().Check(Ctx(DocWithContent("BT (hi) Tj ET"))));
        Assert.Equal("ua-content-tagged", f.RuleId);
        Assert.Equal(0, f.PageIndex);
    }

    [Fact]
    public void Untagged_path_paint_is_flagged()
    {
        Assert.Single(new UaContentTaggedRule().Check(Ctx(DocWithContent("0 0 10 10 re f"))));
    }

    [Fact]
    public void Text_in_a_tagged_sequence_passes()
    {
        Assert.Empty(new UaContentTaggedRule().Check(
            Ctx(DocWithContent("/P <</MCID 0>> BDC BT (hi) Tj ET EMC"))));
    }

    [Fact]
    public void Content_marked_as_artifact_passes()
    {
        Assert.Empty(new UaContentTaggedRule().Check(
            Ctx(DocWithContent("/Artifact BMC 0 0 10 10 re f EMC"))));
    }

    [Fact]
    public void Content_in_a_tagged_form_xobject_passes()
    {
        // The page draws the form outside any marked content, but the form tags its own content — so nothing
        // is untagged. Guards the Form XObject recursion carrying marked-content depth across the boundary.
        var doc = DocWithContent("/Fm0 Do");
        var form = new PdfStream(
            new PdfDictionary { [N("Type")] = N("XObject"), [N("Subtype")] = N("Form"), [N("BBox")] = Box() },
            Encoding.ASCII.GetBytes("/P <</MCID 0>> BDC 0 0 10 10 re f EMC"));
        doc.AddObject(9, 0, form);
        ((PdfDictionary)doc.Objects[3]!)[N("Resources")] =
            new PdfDictionary { [N("XObject")] = new PdfDictionary { [N("Fm0")] = Ref(9) } };
        Assert.Empty(new UaContentTaggedRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Untagged_content_inside_a_form_xobject_is_flagged()
    {
        // The form is drawn outside any marked content AND does not tag its own content — untagged.
        var doc = DocWithContent("/Fm0 Do");
        var form = new PdfStream(
            new PdfDictionary { [N("Type")] = N("XObject"), [N("Subtype")] = N("Form"), [N("BBox")] = Box() },
            Encoding.ASCII.GetBytes("0 0 10 10 re f"));
        doc.AddObject(9, 0, form);
        ((PdfDictionary)doc.Objects[3]!)[N("Resources")] =
            new PdfDictionary { [N("XObject")] = new PdfDictionary { [N("Fm0")] = Ref(9) } };
        Assert.Single(new UaContentTaggedRule().Check(Ctx(doc)));
    }

    // ── ua-artifact-nesting (7.1) ─────────────────────────────────────────────

    [Fact]
    public void Artifact_nested_in_tagged_content_is_flagged()
    {
        Finding f = Assert.Single(new UaArtifactNestingRule().Check(
            Ctx(DocWithContent("/Span <</MCID 1>> BDC /Artifact BMC 0 0 5 5 re f EMC EMC"))));
        Assert.Equal("ua-artifact-nesting", f.RuleId);
    }

    [Fact]
    public void Tagged_content_nested_in_artifact_is_flagged()
    {
        Assert.Single(new UaArtifactNestingRule().Check(
            Ctx(DocWithContent("/Artifact BMC /P <</MCID 1>> BDC (x) Tj EMC EMC"))));
    }

    [Fact]
    public void Sibling_artifact_and_tagged_sequences_pass()
    {
        Assert.Empty(new UaArtifactNestingRule().Check(
            Ctx(DocWithContent("/Artifact BMC 0 0 5 5 re f EMC /P <</MCID 1>> BDC (x) Tj EMC"))));
    }

    // ── ua-figure-alt (7.3) — content-stream ActualText ───────────────────────

    [Fact]
    public void Figure_with_no_alt_and_no_content_actualtext_is_flagged()
    {
        var figure = new PdfDictionary { [N("S")] = N("Figure"), [N("K")] = new PdfArray(new PdfInteger(1)) };
        var doc = DocFigureWithContent(figure, "/Figure <</MCID 1>> BDC (x) Tj EMC");
        Finding f = Assert.Single(new UaFigureAltRule().Check(Ctx(doc)));
        Assert.Equal("ua-figure-alt", f.RuleId);
    }

    [Fact]
    public void Figure_whose_marked_content_supplies_actualtext_passes()
    {
        var figure = new PdfDictionary { [N("S")] = N("Figure"), [N("K")] = new PdfArray(new PdfInteger(1)) };
        var doc = DocFigureWithContent(figure, "/Figure <</MCID 1 /ActualText (a logo)>> BDC (x) Tj EMC");
        Assert.Empty(new UaFigureAltRule().Check(Ctx(doc)));
    }

    // ── end-to-end ────────────────────────────────────────────────────────────

    [Fact]
    public void Fully_tagged_page_has_no_marked_content_findings()
    {
        PreflightResult result = Preflighter.Check(
            DocWithContent("/P <</MCID 0>> BDC BT (hi) Tj ET EMC /Artifact BMC 0 0 5 5 re f EMC"),
            ConformanceProfile.PdfUA1);
        Assert.DoesNotContain(result.Findings, f =>
            f.RuleId is "ua-content-tagged" or "ua-artifact-nesting");
    }
}
