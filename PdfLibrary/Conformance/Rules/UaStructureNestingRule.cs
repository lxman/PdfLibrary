using System.Linq;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 structure nesting (ISO 14289-1:2014; ISO 32000-1 14.8.4): the table, list, and table-of-contents
/// structure types have fixed parent/child relationships. A cell must sit in a row, a row in a table section,
/// a list item in a list, a TOC item in a TOC, and each of these containers may hold only its permitted child
/// types (with a Caption, where allowed, only as the first child). This rule walks the structure tree via
/// <see cref="StructureTree.Nodes"/> — which exposes each element's immediate parent and child standard types —
/// and reports the first nesting violation on each element. Parent/child types are resolved through
/// <c>/RoleMap</c>, and the parent is the immediate structure parent (grouping elements are not skipped),
/// matching the ISO containment model.
/// </summary>
internal sealed class UaStructureNestingRule : IConformanceRule
{
    public string RuleId => "ua-structure-nesting";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        foreach (StructureNode node in StructureTree.Nodes(context))
        {
            if (Violation(node) is not { } hit)
                continue;

            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, hit.Clause),
                Message = hit.Message,
                ObjectNumber = node.Element.IsIndirect ? node.Element.ObjectNumber : null,
            };
        }
    }

    private static (string Clause, string Message)? Violation(StructureNode node)
    {
        string? parent = node.ParentStandardType;
        IReadOnlyList<string?> kids = node.KidStandardTypes;

        switch (node.StandardType)
        {
            // ── Tables (ISO 14289-1 7.5) ──────────────────────────────────────
            case "Table" when kids.Any(k => k is not ("TR" or "THead" or "TBody" or "TFoot" or "Caption")):
                return ("7.5", "A Table contains an element other than TR, THead, TBody, TFoot or Caption.");
            case "Table" when Count(kids, "THead") > 1:
                return ("7.5", "A Table contains more than one THead (table header row group).");
            case "Table" when Count(kids, "TFoot") > 1:
                return ("7.5", "A Table contains more than one TFoot (table footer row group).");
            case "Table" when Count(kids, "Caption") > 1:
                return ("7.5", "A Table contains more than one Caption.");
            case "Table" when Count(kids, "THead") > 0 && Count(kids, "TBody") == 0:
                return ("7.5", "A Table has a THead but no TBody (table body row group).");
            case "Table" when Count(kids, "TFoot") > 0 && Count(kids, "TBody") == 0:
                return ("7.5", "A Table has a TFoot but no TBody (table body row group).");
            case "Table" when CaptionNotFirstOrLast(kids):
                return ("7.5", "A Table has a Caption that is neither its first nor its last child.");
            case "TR" when parent is not ("Table" or "THead" or "TBody" or "TFoot"):
                return ("7.5", "A TR (table row) is not contained in a Table, THead, TBody or TFoot element.");
            case "TR" when kids.Any(k => k is not ("TH" or "TD")):
                return ("7.5", "A TR (table row) contains an element other than TH or TD.");
            case "TH" when parent != "TR":
                return ("7.5", "A TH (table header cell) is not contained in a TR (table row) element.");
            case "TD" when parent != "TR":
                return ("7.5", "A TD (table data cell) is not contained in a TR (table row) element.");
            case "THead" when parent != "Table":
                return ("7.5", "A THead (table header row group) is not contained in a Table element.");
            case "THead" when kids.Any(k => k != "TR"):
                return ("7.5", "A THead (table header row group) contains an element other than TR.");
            case "TBody" when parent != "Table":
                return ("7.5", "A TBody (table body row group) is not contained in a Table element.");
            case "TBody" when kids.Any(k => k != "TR"):
                return ("7.5", "A TBody (table body row group) contains an element other than TR.");
            case "TFoot" when parent != "Table":
                return ("7.5", "A TFoot (table footer row group) is not contained in a Table element.");
            case "TFoot" when kids.Any(k => k != "TR"):
                return ("7.5", "A TFoot (table footer row group) contains an element other than TR.");

            // ── Lists (ISO 14289-1 7.6) ───────────────────────────────────────
            case "LI" when parent != "L":
                return ("7.6", "An LI (list item) is not contained in an L (list) element.");
            case "LI" when kids.Any(k => k is not ("Lbl" or "LBody")):
                return ("7.6", "An LI (list item) contains an element other than Lbl or LBody.");
            case "LBody" when parent != "LI":
                return ("7.6", "An LBody (list item body) is not contained in an LI (list item) element.");
            case "L" when kids.Any(k => k is not ("L" or "LI" or "Caption")):
                return ("7.6", "An L (list) contains an element other than L, LI or Caption.");
            case "L" when CaptionNotFirst(kids):
                return ("7.6", "An L (list) has a Caption that is not its first child.");

            // ── Table of contents (ISO 14289-1 7.2) ───────────────────────────
            case "TOCI" when parent != "TOC":
                return ("7.2", "A TOCI (table-of-contents item) is not contained in a TOC element.");
            case "TOC" when kids.Any(k => k is not ("TOC" or "TOCI" or "Caption")):
                return ("7.2", "A TOC contains an element other than TOC, TOCI or Caption.");
            case "TOC" when CaptionNotFirst(kids):
                return ("7.2", "A TOC has a Caption that is not its first child.");
        }

        return null;
    }

    // In a list / TOC a Caption is permitted only as the first child; flag one appearing after the first.
    private static bool CaptionNotFirst(IReadOnlyList<string?> kids)
    {
        for (int i = 1; i < kids.Count; i++)
            if (kids[i] == "Caption")
                return true;
        return false;
    }

    // In a table a Caption is permitted as either the first OR the last child; flag one in the interior.
    private static bool CaptionNotFirstOrLast(IReadOnlyList<string?> kids)
    {
        for (int i = 1; i < kids.Count - 1; i++)
            if (kids[i] == "Caption")
                return true;
        return false;
    }

    private static int Count(IReadOnlyList<string?> kids, string type)
    {
        int n = 0;
        foreach (string? k in kids)
            if (k == type)
                n++;
        return n;
    }
}
