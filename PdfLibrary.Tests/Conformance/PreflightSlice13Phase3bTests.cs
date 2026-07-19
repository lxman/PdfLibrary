using System.Linq;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 13 (PDF/UA-1) phase 3b — structure-tree relationship semantics (ISO 32000-1 14.8.4):
/// standard-type / role-map coverage (<see cref="UaStandardTypeRule"/>) and the table / list / TOC nesting
/// rules (<see cref="UaStructureNestingRule"/>), both driven by the parent-aware
/// <see cref="StructureTree.Nodes"/> walk. The veraPDF PDF_UA-1 corpus backs the red/green surface
/// (<see cref="CorpusOracleTests"/>); these are the hand-built edge cases.
/// </summary>
public class PreflightSlice13Phase3bTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfString Str(string s) => new(System.Text.Encoding.ASCII.GetBytes(s));
    private static ConformanceContext Ctx(PdfDocument doc) => new(doc, ConformanceProfile.PdfUA1);

    /// <summary>A structure element with type <paramref name="s"/> and the given /K children.</summary>
    private static PdfDictionary Elem(string s, params PdfObject[] kids) => new()
    {
        [N("Type")] = N("StructElem"),
        [N("S")] = N(s),
        [N("K")] = new PdfArray(kids),
    };

    /// <summary>A document with a structure tree whose root /K and element objects <paramref name="build"/>
    /// populates; the rules read the tree through the catalog /StructTreeRoot.</summary>
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

    // ── ua-standard-type (7.1) ────────────────────────────────────────────────

    [Fact]
    public void Standard_type_passes()
    {
        var doc = StructDoc((d, root) => { d.AddObject(50, 0, Elem("P", new PdfInteger(0))); root[N("K")] = Ref(50); });
        Assert.Empty(new UaStandardTypeRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Nonstandard_type_without_rolemap_is_flagged()
    {
        var doc = StructDoc((d, root) => { d.AddObject(50, 0, Elem("Chapter")); root[N("K")] = Ref(50); });
        Finding f = Assert.Single(new UaStandardTypeRule().Check(Ctx(doc)));
        Assert.Equal("ua-standard-type", f.RuleId);
    }

    [Fact]
    public void Nonstandard_type_rolemapped_to_standard_passes()
    {
        var doc = StructDoc((d, root) =>
        {
            root[N("RoleMap")] = new PdfDictionary { [N("Chapter")] = N("Sect") };
            d.AddObject(50, 0, Elem("Chapter"));
            root[N("K")] = Ref(50);
        });
        Assert.Empty(new UaStandardTypeRule().Check(Ctx(doc)));
    }

    [Fact] // the corpus 7.1-t05 case: RoleMap maps to lowercase "p", which is not the standard "P"
    public void Rolemapped_to_nonstandard_type_is_flagged()
    {
        var doc = StructDoc((d, root) =>
        {
            root[N("RoleMap")] = new PdfDictionary { [N("Standard")] = N("p") };
            d.AddObject(50, 0, Elem("Standard"));
            root[N("K")] = Ref(50);
        });
        Assert.Single(new UaStandardTypeRule().Check(Ctx(doc)));
    }

    // ── ua-structure-nesting: tables (7.5) ────────────────────────────────────

    [Fact]
    public void Wellformed_table_passes()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(70, 0, Elem("TD", new PdfInteger(0)));
            d.AddObject(60, 0, Elem("TR", Ref(70)));
            d.AddObject(55, 0, Elem("TBody", Ref(60)));
            d.AddObject(50, 0, Elem("Table", Ref(55)));
            root[N("K")] = Ref(50);
        });
        Assert.Empty(new UaStructureNestingRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Tr_not_in_table_section_is_flagged()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(60, 0, Elem("TR", new PdfInteger(0)));
            d.AddObject(50, 0, Elem("Document", Ref(60)));
            root[N("K")] = Ref(50);
        });
        Finding f = Assert.Single(new UaStructureNestingRule().Check(Ctx(doc)));
        Assert.Contains("TR", f.Message);
    }

    [Fact]
    public void Th_not_in_row_is_flagged()
    {
        // TH directly under THead (no intervening TR) — the corpus 7.2-t08 case.
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(60, 0, Elem("TH", new PdfInteger(0)));
            d.AddObject(55, 0, Elem("THead", Ref(60)));
            d.AddObject(50, 0, Elem("Table", Ref(55)));
            root[N("K")] = Ref(50);
        });
        // The malformed THead→TH (no TR) trips both the TH parent rule and the THead child rule.
        Assert.Contains(new UaStructureNestingRule().Check(Ctx(doc)),
            f => f.Message.Contains("table header cell"));
    }

    [Fact]
    public void Row_group_not_in_table_is_flagged()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(55, 0, Elem("THead", new PdfInteger(0)));
            d.AddObject(50, 0, Elem("Document", Ref(55)));
            root[N("K")] = Ref(50);
        });
        Assert.Single(new UaStructureNestingRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Tr_containing_a_span_is_flagged()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(70, 0, Elem("Span", new PdfInteger(0)));
            d.AddObject(60, 0, Elem("TR", Ref(70)));
            d.AddObject(50, 0, Elem("Table", Ref(60)));
            root[N("K")] = Ref(50);
        });
        Assert.Contains(new UaStructureNestingRule().Check(Ctx(doc)),
            f => f.Message.Contains("TR") && f.Message.Contains("other than"));
    }

    // ── ua-structure-nesting: table cardinality / caption (7.5) ───────────────

    [Fact]
    public void Table_with_all_sections_and_leading_caption_passes()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(80, 0, Elem("TD", new PdfInteger(0)));
            d.AddObject(70, 0, Elem("TR", Ref(80)));
            d.AddObject(60, 0, Elem("TBody", Ref(70)));
            d.AddObject(61, 0, Elem("Caption", new PdfInteger(1)));
            d.AddObject(50, 0, Elem("Table", Ref(61), Ref(60))); // Caption first, then TBody
            root[N("K")] = Ref(50);
        });
        Assert.Empty(new UaStructureNestingRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Table_with_trailing_caption_passes()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(60, 0, Elem("TBody"));
            d.AddObject(61, 0, Elem("Caption", new PdfInteger(0)));
            d.AddObject(50, 0, Elem("Table", Ref(60), Ref(61))); // Caption last
            root[N("K")] = Ref(50);
        });
        Assert.Empty(new UaStructureNestingRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Table_containing_a_paragraph_is_flagged()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(60, 0, Elem("P", new PdfInteger(0)));
            d.AddObject(50, 0, Elem("Table", Ref(60)));
            root[N("K")] = Ref(50);
        });
        Assert.Contains(new UaStructureNestingRule().Check(Ctx(doc)),
            f => f.Message.Contains("Table contains an element other than"));
    }

    [Fact]
    public void Table_with_two_theads_is_flagged()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(60, 0, Elem("THead"));
            d.AddObject(61, 0, Elem("THead"));
            d.AddObject(62, 0, Elem("TBody"));
            d.AddObject(50, 0, Elem("Table", Ref(60), Ref(61), Ref(62)));
            root[N("K")] = Ref(50);
        });
        Assert.Contains(new UaStructureNestingRule().Check(Ctx(doc)), f => f.Message.Contains("more than one THead"));
    }

    [Fact]
    public void Table_with_two_captions_is_flagged()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(60, 0, Elem("Caption", new PdfInteger(0)));
            d.AddObject(61, 0, Elem("TBody"));
            d.AddObject(62, 0, Elem("Caption", new PdfInteger(1)));
            d.AddObject(50, 0, Elem("Table", Ref(60), Ref(61), Ref(62)));
            root[N("K")] = Ref(50);
        });
        Assert.Contains(new UaStructureNestingRule().Check(Ctx(doc)), f => f.Message.Contains("more than one Caption"));
    }

    [Fact]
    public void Table_with_thead_but_no_tbody_is_flagged()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(60, 0, Elem("THead"));
            d.AddObject(50, 0, Elem("Table", Ref(60)));
            root[N("K")] = Ref(50);
        });
        Assert.Contains(new UaStructureNestingRule().Check(Ctx(doc)), f => f.Message.Contains("no TBody"));
    }

    [Fact]
    public void Table_with_caption_in_the_middle_is_flagged()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(60, 0, Elem("THead"));
            d.AddObject(61, 0, Elem("Caption", new PdfInteger(0)));
            d.AddObject(62, 0, Elem("TBody"));
            d.AddObject(50, 0, Elem("Table", Ref(60), Ref(61), Ref(62))); // Caption between THead and TBody
            root[N("K")] = Ref(50);
        });
        Assert.Contains(new UaStructureNestingRule().Check(Ctx(doc)),
            f => f.Message.Contains("Caption that is neither its first nor its last"));
    }

    // ── ua-table-regular: grid / spans (7.5) ──────────────────────────────────

    private static PdfObject I(int n) => new PdfInteger(n);
    private static PdfDictionary Row(params PdfObject[] cells) => Elem("TR", cells);

    private static PdfDictionary Cell(string s, int rowSpan = 1, int colSpan = 1)
    {
        var cell = Elem(s, I(0));
        if (rowSpan != 1 || colSpan != 1)
        {
            var attr = new PdfDictionary { [N("O")] = N("Table") };
            if (rowSpan != 1) attr[N("RowSpan")] = I(rowSpan);
            if (colSpan != 1) attr[N("ColSpan")] = I(colSpan);
            cell[N("A")] = attr;
        }
        return cell;
    }

    private static PdfDocument TableDoc(params PdfObject[] rows)
    {
        return StructDoc((d, root) =>
        {
            d.AddObject(50, 0, Elem("Table", rows));
            root[N("K")] = Ref(50);
        });
    }

    [Fact]
    public void Regular_grid_passes()
    {
        var doc = TableDoc(
            Row(Cell("TH"), Cell("TH")),
            Row(Cell("TD"), Cell("TD")));
        Assert.Empty(new UaTableGridRule().Check(Ctx(doc)));
    }

    [Fact] // the 7.2-t15-pass-a shape: a ColSpan and RowSpans that still tile a clean rectangle
    public void Valid_spanned_grid_passes()
    {
        var doc = TableDoc(
            Row(Cell("TH", colSpan: 2), Cell("TD", rowSpan: 2)), // covers (0,0)(0,1) and (0,2)(1,2)
            Row(Cell("TD"), Cell("TD")));                         // covers (1,0)(1,1); (1,2) from the rowspan
        Assert.Empty(new UaTableGridRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Rowspan_past_the_last_row_is_flagged()
    {
        var doc = TableDoc(
            Row(Cell("TH", rowSpan: 3), Cell("TD")), // rowspan 3 in a 2-row table
            Row(Cell("TD")));
        Assert.Contains(new UaTableGridRule().Check(Ctx(doc)), f => f.RuleId == "ua-table-regular");
    }

    [Fact]
    public void Ragged_rows_are_flagged()
    {
        var doc = TableDoc(
            Row(Cell("TH"), Cell("TH")), // 2 columns
            Row(Cell("TD")));            // 1 column — ragged
        Assert.Single(new UaTableGridRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Overlapping_colspan_is_flagged()
    {
        var doc = TableDoc(
            Row(Cell("TH", colSpan: 2)),          // covers (0,0)(0,1) → 2 columns
            Row(Cell("TD"), Cell("TD"), Cell("TD"))); // 3 columns → column count mismatch
        Assert.Single(new UaTableGridRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Zero_colspan_is_flagged()
    {
        // A malformed ColSpan of 0 leaves the row short of columns — irregular, as veraPDF treats it (it uses
        // the raw span, not a clamp). The cell occupies no column, so (1,1) is left empty.
        var doc = TableDoc(
            Row(Cell("TH"), Cell("TH")),
            Row(Cell("TD", colSpan: 0), Cell("TD")));
        Assert.Single(new UaTableGridRule().Check(Ctx(doc)));
    }

    // ── ua-structure-nesting: lists (7.6) ─────────────────────────────────────

    [Fact]
    public void Wellformed_list_passes()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(70, 0, Elem("LBody", new PdfInteger(0)));
            d.AddObject(60, 0, Elem("LI", Ref(70)));
            d.AddObject(50, 0, Elem("L", Ref(60)));
            root[N("K")] = Ref(50);
        });
        Assert.Empty(new UaStructureNestingRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Li_not_in_list_is_flagged()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(60, 0, Elem("LI", new PdfInteger(0)));
            d.AddObject(50, 0, Elem("Document", Ref(60)));
            root[N("K")] = Ref(50);
        });
        Assert.Single(new UaStructureNestingRule().Check(Ctx(doc)));
    }

    [Fact]
    public void List_with_caption_not_first_is_flagged()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(61, 0, Elem("LI", new PdfInteger(0)));
            d.AddObject(62, 0, Elem("Caption", new PdfInteger(1)));
            d.AddObject(50, 0, Elem("L", Ref(61), Ref(62))); // Caption as second child
            root[N("K")] = Ref(50);
        });
        Assert.Contains(new UaStructureNestingRule().Check(Ctx(doc)), f => f.Message.Contains("Caption"));
    }

    // ── ua-structure-nesting: TOC (7.2) ───────────────────────────────────────

    [Fact]
    public void Toci_not_in_toc_is_flagged()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(60, 0, Elem("TOCI", new PdfInteger(0)));
            d.AddObject(50, 0, Elem("NonStruct", Ref(60))); // the corpus 7.2-t26 case: TOCI under NonStruct
            root[N("K")] = Ref(50);
        });
        Assert.Single(new UaStructureNestingRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Wellformed_toc_passes()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(60, 0, Elem("TOCI", new PdfInteger(0)));
            d.AddObject(50, 0, Elem("TOC", Ref(60)));
            root[N("K")] = Ref(50);
        });
        Assert.Empty(new UaStructureNestingRule().Check(Ctx(doc)));
    }

    // ── ua-attribute-lang: natural language for Alt/ActualText/E (7.2) ─────────

    private static PdfDictionary FigureWithAlt() => new()
    {
        [N("Type")] = N("StructElem"), [N("S")] = N("Figure"),
        [N("Alt")] = Str("a bar chart"), [N("K")] = new PdfArray(new PdfInteger(0)),
    };

    private static void SetCatalogLang(PdfDocument doc, string lang) =>
        ((PdfDictionary)doc.Objects[1]!)[N("Lang")] = Str(lang);

    [Fact]
    public void Alt_with_no_language_anywhere_is_flagged()
    {
        var doc = StructDoc((d, root) => { d.AddObject(50, 0, FigureWithAlt()); root[N("K")] = Ref(50); });
        Finding f = Assert.Single(new UaTextAttributeLangRule().Check(Ctx(doc)));
        Assert.Equal("ua-attribute-lang", f.RuleId);
    }

    [Fact]
    public void Alt_with_catalog_language_passes()
    {
        var doc = StructDoc((d, root) => { d.AddObject(50, 0, FigureWithAlt()); root[N("K")] = Ref(50); });
        SetCatalogLang(doc, "en-US");
        Assert.Empty(new UaTextAttributeLangRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Alt_with_element_language_passes()
    {
        var doc = StructDoc((d, root) =>
        {
            var fig = FigureWithAlt();
            fig[N("Lang")] = Str("en-US");
            d.AddObject(50, 0, fig);
            root[N("K")] = Ref(50);
        });
        Assert.Empty(new UaTextAttributeLangRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Alt_with_ancestor_language_passes()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(50, 0, FigureWithAlt());
            d.AddObject(40, 0, new PdfDictionary
            {
                [N("Type")] = N("StructElem"), [N("S")] = N("Document"),
                [N("Lang")] = Str("en-US"), [N("K")] = new PdfArray(Ref(50)),
            });
            root[N("K")] = Ref(40);
        });
        Assert.Empty(new UaTextAttributeLangRule().Check(Ctx(doc)));
    }

    [Fact]
    public void ActualText_with_no_language_is_flagged()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(50, 0, new PdfDictionary
            {
                [N("Type")] = N("StructElem"), [N("S")] = N("H1"),
                [N("ActualText")] = Str("Chapter 1"), [N("K")] = new PdfArray(new PdfInteger(0)),
            });
            root[N("K")] = Ref(50);
        });
        Assert.Single(new UaTextAttributeLangRule().Check(Ctx(doc)));
    }

    [Fact]
    public void Element_without_text_attribute_is_not_flagged()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(50, 0, Elem("P", new PdfInteger(0))); // no Alt/ActualText/E
            root[N("K")] = Ref(50);
        });
        Assert.Empty(new UaTextAttributeLangRule().Check(Ctx(doc)));
    }

    // ── ua-object-lang: outline (bookmark) natural language (7.2) ──────────────

    private static PdfDocument OutlineDoc(bool outlines, bool catalogLang)
    {
        var doc = new PdfDocument();
        var catalog = new PdfDictionary { [N("Type")] = N("Catalog") };
        if (catalogLang) catalog[N("Lang")] = Str("en-US");
        if (outlines)
        {
            doc.AddObject(20, 0, new PdfDictionary { [N("Title")] = Str("Chapter 1") });
            doc.AddObject(10, 0, new PdfDictionary
            {
                [N("Type")] = N("Outlines"), [N("First")] = Ref(20), [N("Last")] = Ref(20),
            });
            catalog[N("Outlines")] = Ref(10);
        }
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    [Fact]
    public void Outlines_without_catalog_language_are_flagged()
    {
        Finding f = Assert.Single(new UaObjectLangRule().Check(Ctx(OutlineDoc(outlines: true, catalogLang: false))));
        Assert.Equal("ua-object-lang", f.RuleId);
    }

    [Fact]
    public void Outlines_with_catalog_language_pass()
    {
        Assert.Empty(new UaObjectLangRule().Check(Ctx(OutlineDoc(outlines: true, catalogLang: true))));
    }

    [Fact]
    public void No_outlines_is_not_flagged_even_without_catalog_language()
    {
        Assert.Empty(new UaObjectLangRule().Check(Ctx(OutlineDoc(outlines: false, catalogLang: false))));
    }

    // ── ua-language-tag: /Lang value syntax (ISO 32000-1 14.9) ────────────────

    private static PdfDocument LangDoc(PdfObject lang)
    {
        var doc = new PdfDocument();
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Lang")] = lang });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    [Theory]
    [InlineData("en")]
    [InlineData("en-US")]
    [InlineData("zh-Hans-CN")]
    [InlineData("x-klingon")]
    public void Valid_language_identifiers_pass(string tag)
    {
        Assert.Empty(new UaLanguageTagRule().Check(Ctx(LangDoc(Str(tag)))));
    }

    [Theory]
    [InlineData("portugues-pt")] // primary subtag longer than 8 letters
    [InlineData("1-pt")]         // primary subtag starts with a digit
    [InlineData("-pt")]          // leading hyphen (empty primary subtag)
    [InlineData("nl-1234abcde")] // second subtag longer than 8
    [InlineData("")]             // empty value
    public void Malformed_language_identifiers_are_flagged(string tag)
    {
        Finding f = Assert.Single(new UaLanguageTagRule().Check(Ctx(LangDoc(Str(tag)))));
        Assert.Equal("ua-language-tag", f.RuleId);
    }

    [Fact] // the corpus 7.2-t29-fail-k case: a UTF-16BE /Lang whose letters are Cyrillic, not ASCII
    public void Utf16_language_with_non_ascii_letters_is_flagged()
    {
        // FE FF (BOM) + U+043F U+0442 (Cyrillic "пт") + "-PT" → "пт-PT"
        var utf16 = new PdfString(new byte[] { 0xFE, 0xFF, 0x04, 0x3F, 0x04, 0x42, 0x00, 0x2D, 0x00, 0x50, 0x00, 0x54 });
        Assert.Single(new UaLanguageTagRule().Check(Ctx(LangDoc(utf16))));
    }

    [Fact] // a malformed /Lang on a structure element (not just the catalog) is caught by the object scan
    public void Malformed_structure_element_language_is_flagged()
    {
        var doc = LangDoc(Str("en-US")); // valid catalog language
        doc.AddObject(50, 0, new PdfDictionary
        {
            [N("Type")] = N("StructElem"), [N("S")] = N("P"), [N("Lang")] = Str("portugues-pt"),
        });
        Assert.Single(new UaLanguageTagRule().Check(Ctx(doc)));
    }
}
