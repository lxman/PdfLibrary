using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 table header association (ISO 14289-1:2014, 7.5; ISO 32000-2 14.8.4.8.3), calibrated against
/// veraPDF's <c>SETD</c> rules (<c>hasConnectedHeader != false || unknownHeaders …</c>). In a <b>regular</b>
/// table (rectangular once RowSpan/ColSpan are honoured — the same grid <see cref="UaTableGridRule"/> builds)
/// every data cell (TD) must be associated with a header. A cell is connected when either:
/// <list type="bullet">
///   <item>it carries a <c>/Headers</c> attribute whose every id resolves to a TH <c>/ID</c> in the table; or</item>
///   <item>a header can be found algorithmically — a TH with an explicit <c>Scope</c> above it (Column) or to
///     its left (Row). <b>PDF/UA-1 has no positional default scope</b> (veraPDF's <c>getDefaultScope</c>
///     returns null for UA-1), so an unscoped TH does not connect a cell.</item>
/// </list>
/// A table is exempt when it is irregular, contains no TH, or every TH carries a Scope attribute (whatever its
/// value) — mirroring veraPDF, which only sets <c>hasConnectedHeader</c> in the remaining case. veraPDF flags
/// the first unconnected TD in document (row-major) order, so this reports one finding per table. The check
/// runs only when the file is Tagged; an untagged file is already reported by <see cref="UaTaggedRule"/>.
/// </summary>
internal sealed class UaTableHeaderRule : IConformanceRule
{
    public string RuleId => "ua-table-header";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    private const string Both = "Both";
    private const string ColumnScope = "Column";
    private const string RowScope = "Row";

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        // Table attributes (Scope, Headers, spans) may live in a /C class shared through the /ClassMap.
        PdfDictionary? classMap = LogicalStructure.StructTreeRootDictionary(context.Document) is { } root
            && context.Resolve(root.Get("ClassMap")) is PdfDictionary map ? map : null;

        foreach (StructureNode node in LogicalStructure.Nodes(context.Document))
        {
            if (node.StandardType != "Table")
                continue;
            if (Analyze(context, node.Element, classMap) is not { } message)
                continue;

            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "7.5"),
                Message = message,
                ObjectNumber = node.Element.IsIndirect ? node.Element.ObjectNumber : null,
            };
        }
    }

    // The finding message for the first unconnected TD, or null when the table is compliant or exempt.
    private static string? Analyze(ConformanceContext context, PdfDictionary table, PdfDictionary? classMap)
    {
        if (BuildGrid(context, table, classMap) is not { } grid)
            return null; // empty or irregular — headers are not algorithmically determinable, so exempt

        Cell[,] cells = grid.Cells;
        int rows = grid.Rows, cols = grid.Cols;

        // Collect every TH /ID (an empty id is not a valid target) and note whether every TH carries a Scope.
        var idSet = new HashSet<string>();
        bool everyHeaderScoped = true;
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            Cell cell = cells[r, c];
            if (!cell.IsHeader)
                continue;
            if (cell.Id is { Length: > 0 })
                idSet.Add(cell.Id);
            if (cell.Scope is null)
                everyHeaderScoped = false;
        }

        if (everyHeaderScoped)
            return null; // every TH is scoped (or the table has no TH) — the scope mechanism covers it

        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            Cell cell = cells[r, c];
            if (cell.IsHeader || r != cell.OriginRow || c != cell.OriginCol || (r == 0 && c == 0))
                continue; // only a TD, at its origin; veraPDF skips the (0,0) cell
            if (IsConnected(cells, rows, cols, cell, idSet))
                continue;

            List<string> unknown = cell.Headers.Where(h => !idSet.Contains(h)).ToList();
            return unknown.Count == 0
                ? "A table data cell (TD) has no /Headers attribute and no header can be determined for it "
                  + "algorithmically (no TH with a Scope attribute heads its column or row)."
                : "A table data cell (TD) has a /Headers attribute that references undefined header id(s) "
                  + $"'{string.Join("', '", unknown)}', and no header can be determined for it algorithmically.";
        }
        return null;
    }

    // A TD is connected when its /Headers all resolve to a TH id in the table, or a scoped TH heads it.
    private static bool IsConnected(Cell[,] cells, int rows, int cols, Cell td, HashSet<string> idSet)
    {
        if (td.Headers.Count > 0 && td.Headers.All(idSet.Contains))
            return true;
        return HasScopedHeader(cells, rows, cols, td);
    }

    // Walk up each spanned column for a Column-scope TH, then left along each spanned row for a Row-scope TH.
    // Once a TH is seen in a line, the walk stops at the first following non-TH cell (veraPDF's headerFound break).
    private static bool HasScopedHeader(Cell[,] cells, int rows, int cols, Cell td)
    {
        int endRow = Math.Min(td.OriginRow + td.RowSpan, rows);
        int endCol = Math.Min(td.OriginCol + td.ColSpan, cols);

        if (td.OriginRow > 0)
            for (int col = td.OriginCol; col < endCol; col++)
            {
                bool headerFound = false;
                for (int r = td.OriginRow - 1; r >= 0; r--)
                {
                    if (ScopeMatches(cells[r, col], ColumnScope))
                        return true;
                    if (cells[r, col].IsHeader)
                        headerFound = true;
                    else if (headerFound)
                        break;
                }
            }

        if (td.OriginCol > 0)
            for (int r = td.OriginRow; r < endRow; r++)
            {
                bool headerFound = false;
                for (int col = td.OriginCol - 1; col >= 0; col--)
                {
                    if (ScopeMatches(cells[r, col], RowScope))
                        return true;
                    if (cells[r, col].IsHeader)
                        headerFound = true;
                    else if (headerFound)
                        break;
                }
            }

        return false;
    }

    // PDF/UA-1: a TH heads a cell only through an explicit Scope of Both or the wanted direction.
    private static bool ScopeMatches(Cell cell, string want) =>
        cell.IsHeader && (cell.Scope == Both || cell.Scope == want);

    // Lays the table onto a rectangular grid honouring RowSpan/ColSpan; returns null when the table is empty or
    // not a clean rectangle (cells overlap, a span runs past the last row, or a slot is left empty) — the same
    // regularity condition UaTableGridRule enforces, which is veraPDF's precondition for the header check.
    private static Grid? BuildGrid(ConformanceContext context, PdfDictionary table, PdfDictionary? classMap)
    {
        var rowElems = Rows(context, table).ToList();
        int numRows = rowElems.Count;
        if (numRows == 0)
            return null;

        var occupied = new Dictionary<(int Row, int Col), Cell>();
        bool irregular = false;
        int numCols = 0;

        for (int r = 0; r < numRows; r++)
        {
            int col = 0;
            foreach (PdfDictionary cellDict in Cells(context, rowElems[r]))
            {
                while (occupied.ContainsKey((r, col)))
                    col++;

                var cell = new Cell
                {
                    IsHeader = LogicalStructure.StandardType(context.Document, cellDict) == "TH",
                    OriginRow = r,
                    OriginCol = col,
                    RowSpan = IntAttribute(context, cellDict, "RowSpan", classMap),
                    ColSpan = IntAttribute(context, cellDict, "ColSpan", classMap),
                    Id = Id(context, cellDict),
                    Scope = NameAttribute(context, cellDict, "Scope", classMap),
                    Headers = ArrayAttribute(context, cellDict, "Headers", classMap),
                };
                int rowSpan = cell.RowSpan, colSpan = cell.ColSpan;

                for (int dr = 0; dr < rowSpan; dr++)
                {
                    if (r + dr >= numRows)
                        irregular = true; // a row-span running past the last row
                    for (int dc = 0; dc < colSpan; dc++)
                        if (!occupied.TryAdd((r + dr, col + dc), cell))
                            irregular = true; // two cells claim the same slot
                }

                col += colSpan;
                if (col > numCols)
                    numCols = col;
            }
        }

        if (irregular)
            return null;

        var cells = new Cell[numRows, numCols];
        for (int r = 0; r < numRows; r++)
        for (int c = 0; c < numCols; c++)
        {
            if (!occupied.TryGetValue((r, c), out Cell? cell))
                return null; // a ragged grid — a row short of columns or a column short of rows
            cells[r, c] = cell;
        }
        return new Grid(cells, numRows, numCols);
    }

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

    // RowSpan/ColSpan default to 1; a value below 1 (malformed) is clamped to 1 (a safe over-count that only
    // makes the grid larger — a genuinely short span leaves an empty slot, which BuildGrid treats as irregular).
    private static int IntAttribute(ConformanceContext context, PdfDictionary cell, string key,
        PdfDictionary? classMap)
    {
        foreach (PdfDictionary attr in TableAttributes(context, cell, classMap))
            if (context.Resolve(attr.Get(key)) is PdfInteger i)
                return Math.Max(1, i.Value);
        return 1;
    }

    private static string? Id(ConformanceContext context, PdfDictionary cell) =>
        context.Resolve(cell.Get("ID")) is PdfString s ? Encoding.Latin1.GetString(s.Bytes) : null;

    private static string? NameAttribute(ConformanceContext context, PdfDictionary cell, string key,
        PdfDictionary? classMap)
    {
        foreach (PdfDictionary attr in TableAttributes(context, cell, classMap))
            if (context.Resolve(attr.Get(key)) is PdfName n)
                return n.Value;
        return null;
    }

    private static List<string> ArrayAttribute(ConformanceContext context, PdfDictionary cell, string key,
        PdfDictionary? classMap)
    {
        foreach (PdfDictionary attr in TableAttributes(context, cell, classMap))
            if (context.Resolve(attr.Get(key)) is PdfArray array)
                return array
                    .Select(context.Resolve)
                    .OfType<PdfString>()
                    .Select(s => Encoding.Latin1.GetString(s.Bytes))
                    .ToList();
        return [];
    }

    // The cell's Table-owned attribute dictionaries, in veraPDF's resolution order: the /A entry (a dictionary
    // or an array of them) first, then the attribute objects named by the cell's /C class(es) via the
    // structure tree's /ClassMap. Only dictionaries whose /O owner is Table are yielded; a reader takes the
    // first that carries the attribute, so /A takes precedence over /C.
    private static IEnumerable<PdfDictionary> TableAttributes(ConformanceContext context, PdfDictionary cell,
        PdfDictionary? classMap)
    {
        foreach (PdfDictionary dict in OwnedByTable(context, cell.Get("A")))
            yield return dict;

        if (classMap is null)
            yield break;

        switch (context.Resolve(cell.Get("C")))
        {
            case PdfName name:
                foreach (PdfDictionary dict in OwnedByTable(context, classMap.Get(name.Value)))
                    yield return dict;
                break;
            case PdfArray classes:
                foreach (PdfObject entry in classes)
                    if (context.Resolve(entry) is PdfName name)
                        foreach (PdfDictionary dict in OwnedByTable(context, classMap.Get(name.Value)))
                            yield return dict;
                break;
        }
    }

    // The Table-owner attribute dictionaries in an /A- or ClassMap-style value: a single dictionary or an array.
    private static IEnumerable<PdfDictionary> OwnedByTable(ConformanceContext context, PdfObject? value)
    {
        switch (context.Resolve(value))
        {
            case PdfDictionary dict when context.ResolveName(dict.Get("O")) == "Table":
                yield return dict;
                break;
            case PdfArray array:
                foreach (PdfObject entry in array)
                    if (context.Resolve(entry) is PdfDictionary dict && context.ResolveName(dict.Get("O")) == "Table")
                        yield return dict;
                break;
        }
    }

    private sealed class Cell
    {
        public required bool IsHeader { get; init; }
        public required int OriginRow { get; init; }
        public required int OriginCol { get; init; }
        public required int RowSpan { get; init; }
        public required int ColSpan { get; init; }
        public required string? Id { get; init; }
        public required string? Scope { get; init; }
        public required List<string> Headers { get; init; }
    }

    private sealed record Grid(Cell[,] Cells, int Rows, int Cols);
}
