using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// PDF/UA-1 table header association (ISO 14289-1:2014, 7.5, <see cref="UaTableHeaderRule"/>), calibrated
/// against veraPDF's SETD rules (<c>hasConnectedHeader != false || unknownHeaders …</c>) and its 7.5-t01/t02
/// fixtures. A regular table's data cell (TD) must connect to a header: either an explicit /Headers attribute
/// whose IDs all resolve to a TH /ID in the table, or an algorithmically findable TH — a TH with an explicit
/// Scope (PDF/UA-1 has no positional default scope) above it (Column) or to its left (Row). A table that is
/// irregular, has no TH, or whose every TH carries a Scope attribute is exempt.
/// </summary>
public class UaTableHeaderRuleTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfString Str(string s) => new(Encoding.ASCII.GetBytes(s));
    private static PdfObject I(int n) => new PdfInteger(n);
    private static ConformanceContext Ctx(PdfDocument doc) => new(doc, ConformanceProfile.PdfUA1);
    private static Finding[] Findings(PdfDocument doc) => new UaTableHeaderRule().Check(Ctx(doc)).ToArray();

    private static PdfDictionary Elem(string s, params PdfObject[] kids) => new()
    {
        [N("Type")] = N("StructElem"), [N("S")] = N(s), [N("K")] = new PdfArray(kids),
    };

    private static PdfDictionary Row(params PdfObject[] cells) => Elem("TR", cells);

    /// <summary>A TH/TD cell. <paramref name="id"/> sets the element /ID; <paramref name="scope"/> and
    /// <paramref name="headers"/> sit in a Table-owned /A attribute dictionary.</summary>
    private static PdfDictionary Cell(string s, string? id = null, string? scope = null, string[]? headers = null,
        int rowSpan = 1, int colSpan = 1, string? cls = null)
    {
        var cell = Elem(s, I(0));
        if (id != null) cell[N("ID")] = Str(id);
        if (cls != null) cell[N("C")] = N(cls);
        if (scope != null || headers != null || rowSpan != 1 || colSpan != 1)
        {
            var attr = new PdfDictionary { [N("O")] = N("Table") };
            if (rowSpan != 1) attr[N("RowSpan")] = I(rowSpan);
            if (colSpan != 1) attr[N("ColSpan")] = I(colSpan);
            if (scope != null) attr[N("Scope")] = N(scope);
            if (headers != null) attr[N("Headers")] = new PdfArray(headers.Select(h => (PdfObject)Str(h)).ToArray());
            cell[N("A")] = attr;
        }
        return cell;
    }

    private static PdfDocument TableDoc(params PdfObject[] rows) => TableDocCM(null, rows);

    private static PdfDocument TableDocCM(PdfDictionary? classMap, params PdfObject[] rows)
    {
        var doc = new PdfDocument();
        var root = new PdfDictionary { [N("Type")] = N("StructTreeRoot") };
        if (classMap != null) root[N("ClassMap")] = classMap;
        doc.AddObject(50, 0, Elem("Table", rows));
        root[N("K")] = Ref(50);
        doc.AddObject(31, 0, root);
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("StructTreeRoot")] = Ref(31) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    // ── exemptions (no finding) ───────────────────────────────────────────────

    [Fact]
    public void A_table_whose_every_TH_has_a_scope_passes()
    {
        // 7.5-t01-pass-a shape: every TH carries a Scope attribute → table-level exemption.
        var doc = TableDoc(
            Row(Cell("TH", scope: "Column"), Cell("TH", scope: "Column")),
            Row(Cell("TH", scope: "Row"), Cell("TD")));
        Assert.Empty(Findings(doc));
    }

    [Fact]
    public void A_table_with_no_TH_cells_passes()
    {
        var doc = TableDoc(Row(Cell("TD"), Cell("TD")), Row(Cell("TD"), Cell("TD")));
        Assert.Empty(Findings(doc));
    }

    [Fact]
    public void A_single_data_cell_table_passes()
    {
        Assert.Empty(Findings(TableDoc(Row(Cell("TD")))));
    }

    [Fact]
    public void An_irregular_table_is_exempt_from_the_header_check()
    {
        // Ragged (row 0 has two columns, row 1 has one) — headers are not algorithmically determinable.
        var doc = TableDoc(Row(Cell("TH"), Cell("TH")), Row(Cell("TD")));
        Assert.Empty(Findings(doc));
    }

    [Fact]
    public void A_TD_whose_headers_all_resolve_passes()
    {
        // 7.5-t01-pass-b shape: /Headers list every referenced TH /ID present in the table.
        var doc = TableDoc(
            Row(Cell("TH", id: "C0"), Cell("TH", id: "C1")),
            Row(Cell("TH", id: "R0"), Cell("TD", headers: ["R0", "C1"])));
        Assert.Empty(Findings(doc));
    }

    [Fact]
    public void A_TD_under_a_column_scope_header_passes()
    {
        // Not all THs are scoped (so no table-level exemption), but the one TD sits under a Column-scope TH.
        var doc = TableDoc(
            Row(Cell("TH", scope: "Column"), Cell("TH", scope: "Column")),
            Row(Cell("TD"), Cell("TH")));
        Assert.Empty(Findings(doc));
    }

    [Fact]
    public void A_TD_right_of_a_row_scope_header_passes()
    {
        // Single row: a Row-scope TH, a TD to its right, and an unscoped TH (defeats table-level exemption).
        var doc = TableDoc(Row(Cell("TH", scope: "Row"), Cell("TD"), Cell("TH")));
        Assert.Empty(Findings(doc));
    }

    [Fact]
    public void A_TD_whose_headers_come_from_a_class_map_passes()
    {
        // veraPDF resolves table attributes from /C class names via the StructTreeRoot /ClassMap, not just /A.
        var classMap = new PdfDictionary
        {
            [N("HdrClass")] = new PdfDictionary
            {
                [N("O")] = N("Table"), [N("Headers")] = new PdfArray(Str("R0"), Str("C1")),
            },
        };
        var doc = TableDocCM(classMap,
            Row(Cell("TH", id: "C0"), Cell("TH", id: "C1")),
            Row(Cell("TH", id: "R0"), Cell("TD", cls: "HdrClass")));
        Assert.Empty(Findings(doc));
    }

    [Fact]
    public void TH_scope_from_a_class_map_exempts_the_table()
    {
        // Every TH's Scope lives in a /C class; without it the table would fail (TDs have no headers).
        var classMap = new PdfDictionary
        {
            [N("ColClass")] = new PdfDictionary { [N("O")] = N("Table"), [N("Scope")] = N("Column") },
        };
        var doc = TableDocCM(classMap,
            Row(Cell("TH", cls: "ColClass"), Cell("TH", cls: "ColClass")),
            Row(Cell("TH", cls: "ColClass"), Cell("TD")));
        Assert.Empty(Findings(doc));
    }

    // ── violations (one finding) ──────────────────────────────────────────────

    [Fact]
    public void A_TD_with_no_resolvable_header_is_flagged()
    {
        // 7.5-t01-fail shape: THs have neither Scope nor ID, TD has no Headers → not determinable.
        var doc = TableDoc(
            Row(Cell("TH"), Cell("TH")),
            Row(Cell("TH"), Cell("TD")));
        Finding f = Assert.Single(Findings(doc));
        Assert.Equal("ua-table-header", f.RuleId);
        Assert.Equal(ConformanceClauses.For(ConformanceProfile.PdfUA1, "7.5"), f.Clause);
    }

    [Fact]
    public void A_TD_referencing_an_undefined_header_is_flagged_with_the_id()
    {
        // 7.5-t02-fail-a shape: /Headers references an ID no TH in the table declares.
        var doc = TableDoc(
            Row(Cell("TH", id: "C0"), Cell("TH", id: "C1")),
            Row(Cell("TH", id: "R0"), Cell("TD", headers: ["12345"])));
        Finding f = Assert.Single(Findings(doc));
        Assert.Equal("ua-table-header", f.RuleId);
        Assert.Contains("12345", f.Message);
    }

    [Fact]
    public void An_empty_string_TH_id_is_not_a_valid_header_target()
    {
        // A TH whose /ID is the empty string is not added to the id set; a TD referencing "" is unconnected.
        var doc = TableDoc(
            Row(Cell("TH", id: ""), Cell("TH", id: "C1")),
            Row(Cell("TH", id: "R0"), Cell("TD", headers: ["R0", ""])));
        Assert.Single(Findings(doc));
    }
}
