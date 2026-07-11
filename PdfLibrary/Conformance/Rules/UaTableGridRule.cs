using System.Linq;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 table regularity (ISO 14289-1:2014, 7.5; ISO 32000-1 14.8.4.3.4): a table's rows and cells,
/// taking <c>RowSpan</c> and <c>ColSpan</c> into account, must form a proper rectangular grid — every row
/// spans the same number of columns, every column the same number of rows, and no two cells overlap. This
/// rule lays each Table's cells (TH/TD) onto a grid, honouring the span attributes read from each cell's
/// <c>/A</c> (attribute) entry, and flags a table whose cells intersect, whose spans run past the last row,
/// or that is otherwise not rectangular (the veraPDF <c>SETable</c>/<c>SETableCell</c> span-consistency
/// checks). Row/cell/span reading uses <see cref="LogicalStructure"/>; grouping sections (THead/TBody/TFoot) are
/// flattened into the table's row sequence in document order.
/// </summary>
internal sealed class UaTableGridRule : IConformanceRule
{
    public string RuleId => "ua-table-regular";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        foreach (StructureNode node in LogicalStructure.Nodes(context.Document))
        {
            if (node.StandardType != "Table")
                continue;
            if (BuildGrid(context, node.Element) is not { } issue)
                continue;

            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "7.5"),
                Message = issue,
                ObjectNumber = node.Element.IsIndirect ? node.Element.ObjectNumber : null,
            };
        }
    }

    // Lays the table's cells onto a grid and returns the first regularity problem, or null when the table is a
    // clean rectangle. Cells are placed left-to-right per row, skipping slots already taken by a row-span from
    // an earlier row — the standard table layout, matching the visual grid a consumer reconstructs.
    private static string? BuildGrid(ConformanceContext context, PdfDictionary table)
    {
        var rows = Rows(context, table).ToList();
        int numRows = rows.Count;
        if (numRows == 0)
            return null;

        var occupied = new HashSet<(int Row, int Col)>();
        bool intersects = false;
        bool overflowsRows = false;
        int numCols = 0;

        for (int r = 0; r < numRows; r++)
        {
            int col = 0;
            foreach (PdfDictionary cell in Cells(context, rows[r]))
            {
                while (occupied.Contains((r, col)))
                    col++;

                (int rowSpan, int colSpan) = Spans(context, cell);
                for (int dr = 0; dr < rowSpan; dr++)
                {
                    if (r + dr >= numRows)
                        overflowsRows = true; // a row-span running past the last row of the table
                    for (int dc = 0; dc < colSpan; dc++)
                        if (!occupied.Add((r + dr, col + dc)))
                            intersects = true; // two cells claim the same slot
                }

                col += colSpan;
                if (col > numCols)
                    numCols = col;
            }
        }

        if (intersects)
            return "Table cells overlap: two cells occupy the same grid position (check RowSpan/ColSpan).";
        if (overflowsRows)
            return "A table cell's RowSpan extends beyond the last row of the table.";

        // Every row must cover all columns and every column must cover all rows — otherwise the spans leave
        // the grid ragged (a row short of columns, or a column short of rows).
        for (int r = 0; r < numRows; r++)
            if (Enumerable.Range(0, numCols).Count(c => occupied.Contains((r, c))) != numCols)
                return "Table rows do not all span the same number of columns (check RowSpan/ColSpan).";
        for (int c = 0; c < numCols; c++)
            if (Enumerable.Range(0, numRows).Count(r => occupied.Contains((r, c))) != numRows)
                return "Table columns do not all span the same number of rows (check RowSpan/ColSpan).";

        return null;
    }

    // The table's rows in document order: a TR child is a row; a THead/TBody/TFoot child contributes its TR
    // children as rows.
    private static IEnumerable<PdfDictionary> Rows(ConformanceContext context, PdfDictionary table)
    {
        foreach (PdfDictionary child in LogicalStructure.ChildElements(context.Document, table))
        {
            switch (LogicalStructure.StandardType(context.Document, child))
            {
                case "TR":
                    yield return child;
                    break;
                case "THead" or "TBody" or "TFoot":
                    foreach (PdfDictionary tr in LogicalStructure.ChildElements(context.Document, child))
                        if (LogicalStructure.StandardType(context.Document, tr) is "TR")
                            yield return tr;
                    break;
            }
        }
    }

    private static IEnumerable<PdfDictionary> Cells(ConformanceContext context, PdfDictionary row) =>
        LogicalStructure.ChildElements(context.Document, row)
            .Where(c => LogicalStructure.StandardType(context.Document, c) is "TH" or "TD");

    // RowSpan/ColSpan default to 1. They live in a Table-owned attribute dictionary reached through the cell's
    // /A entry, which may be a single dictionary or an array of them (one per attribute owner).
    private static (int RowSpan, int ColSpan) Spans(ConformanceContext context, PdfDictionary cell)
    {
        int rowSpan = 1, colSpan = 1;
        foreach (PdfDictionary attr in AttributeDicts(context, cell.Get("A")))
        {
            if (context.Resolve(attr.Get("RowSpan")) is PdfInteger rs && rs.Value >= 1)
                rowSpan = rs.Value;
            if (context.Resolve(attr.Get("ColSpan")) is PdfInteger cs && cs.Value >= 1)
                colSpan = cs.Value;
        }
        return (rowSpan, colSpan);
    }

    private static IEnumerable<PdfDictionary> AttributeDicts(ConformanceContext context, PdfObject? attribute)
    {
        switch (context.Resolve(attribute))
        {
            case PdfDictionary dict:
                yield return dict;
                break;
            case PdfArray array:
                foreach (PdfObject entry in array)
                    if (context.Resolve(entry) is PdfDictionary dict)
                        yield return dict;
                break;
        }
    }
}
