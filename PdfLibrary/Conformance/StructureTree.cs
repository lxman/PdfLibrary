using System;
using System.Collections.Generic;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance;

/// <summary>A structure element paired with the standard types of its parent and its structure-element
/// children — the shape the parent/child nesting rules (ISO 32000-1, 14.8.4) need.</summary>
internal readonly record struct StructureNode(
    PdfDictionary Element,
    string? StandardType,               // this element's standard type (role-map resolved)
    string? ParentStandardType,         // its structure parent's standard type, or null at the tree top
    IReadOnlyList<string?> KidStandardTypes); // standard types of its structure-element children, in order

/// <summary>
/// Navigates a Tagged PDF logical structure tree (ISO 32000-1, 14.7) for the PDF/UA rules: it enumerates the
/// structure element dictionaries reachable from the catalog's <c>/StructTreeRoot</c> via <c>/K</c>, skipping
/// the marked-content and object references (<c>/MCR</c>, <c>/OBJR</c>) and integer MCIDs that are leaves, and
/// resolves an element's <c>/S</c> structure type through the tree's <c>/RoleMap</c> to a standard type. The
/// walk is iterative with a node budget and cycle-guards on indirect object number, so a hostile or cyclic
/// tree cannot spin or overflow the stack.
/// </summary>
internal static class StructureTree
{
    /// <summary>Every structure element (a node carrying an <c>/S</c> type) in the tree, in pre-order.</summary>
    public static IEnumerable<PdfDictionary> Elements(ConformanceContext context)
    {
        if (context.Resolve(context.Catalog?.Dictionary.Get("StructTreeRoot")) is not PdfDictionary root)
            yield break;

        var visited = new HashSet<int>();
        var stack = new Stack<PdfObject?>();
        PushKids(context, root, stack);

        for (int budget = 500_000; stack.Count > 0 && budget > 0; budget--)
        {
            if (context.Resolve(stack.Pop()) is not PdfDictionary node)
                continue; // an integer MCID leaf or a non-dictionary
            if (node.IsIndirect && !visited.Add(node.ObjectNumber))
                continue;

            string? nodeType = context.ResolveName(node.Get("Type"));
            if (nodeType is "MCR" or "OBJR")
                continue; // a marked-content or object reference, not a structure element

            if (node.Get("S") is not null)
                yield return node;

            PushKids(context, node, stack);
        }
    }

    /// <summary>The element's structure type resolved through <c>/RoleMap</c> to a standard type, or the raw
    /// <c>/S</c> when it maps to nothing. A standard type is used as-is (standard types are never remapped); a
    /// non-standard type follows the <c>/RoleMap</c> chain until it reaches a standard type or dead-ends,
    /// cycle-guarded.</summary>
    public static string? StandardType(ConformanceContext context, PdfDictionary element)
    {
        string? type = context.ResolveName(element.Get("S"));
        if (type is null || IsStandardType(type))
            return type;

        if (context.Resolve(RoleMapOf(context)) is not PdfDictionary roleMap)
            return type;

        var seen = new HashSet<string>();
        while (!IsStandardType(type) && seen.Add(type) && context.ResolveName(roleMap.Get(type)) is { } mapped)
            type = mapped;
        return type;
    }

    /// <summary>
    /// The standard structure types of ISO 32000-1:2008, 14.8.4 (grouping, block-level, inline-level,
    /// illustration). A type outside this set must be role-mapped to one of these to be meaningful to a
    /// consumer (PDF/UA-1, ISO 14289-1 7.1).
    /// </summary>
    private static readonly IReadOnlySet<string> StandardTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        // Grouping (14.8.4.2)
        "Document", "Part", "Art", "Sect", "Div", "BlockQuote", "Caption", "TOC", "TOCI", "Index",
        "NonStruct", "Private",
        // Paragraphlike / headings (14.8.4.3.2)
        "P", "H", "H1", "H2", "H3", "H4", "H5", "H6",
        // Lists (14.8.4.3.3)
        "L", "LI", "Lbl", "LBody",
        // Tables (14.8.4.3.4)
        "Table", "TR", "TH", "TD", "THead", "TBody", "TFoot",
        // Inline-level (14.8.4.4)
        "Span", "Quote", "Note", "Reference", "BibEntry", "Code", "Link", "Annot",
        "Ruby", "RB", "RT", "RP", "Warichu", "WT", "WP",
        // Illustration (14.8.4.5)
        "Figure", "Formula", "Form",
    };

    /// <summary>True when <paramref name="type"/> is one of the ISO 32000-1 standard structure types.</summary>
    public static bool IsStandardType(string? type) => type is not null && StandardTypes.Contains(type);

    private static PdfObject? RoleMapOf(ConformanceContext context) =>
        context.Resolve(context.Catalog?.Dictionary.Get("StructTreeRoot")) is PdfDictionary root
            ? root.Get("RoleMap")
            : null;

    private static void PushKids(ConformanceContext context, PdfDictionary node, Stack<PdfObject?> stack)
    {
        switch (context.Resolve(node.Get("K")))
        {
            case PdfArray kids:
                foreach (PdfObject kid in kids)
                    stack.Push(kid);
                break;
            case PdfDictionary single:
                stack.Push(single);
                break;
        }
    }

    /// <summary>
    /// Every structure element paired with its parent's and children's standard types, in pre-order. The
    /// parent type is the <em>immediate</em> structure parent (grouping elements such as NonStruct/Div are not
    /// skipped — matching the ISO 32000-1 14.8.4 containment rules that the PDF/UA nesting checks apply). Child
    /// types are the standard types of the element's structure-element children only (integer MCIDs and
    /// <c>/MCR</c>/<c>/OBJR</c> content references are not structure elements and are excluded).
    /// </summary>
    public static IEnumerable<StructureNode> Nodes(ConformanceContext context)
    {
        if (context.Resolve(context.Catalog?.Dictionary.Get("StructTreeRoot")) is not PdfDictionary root)
            yield break;

        var visited = new HashSet<int>();
        var stack = new Stack<(PdfObject? Node, string? ParentType)>();
        foreach (PdfObject kid in KidObjects(context, root))
            stack.Push((kid, null)); // the tree's top elements have no structure parent

        for (int budget = 500_000; stack.Count > 0 && budget > 0; budget--)
        {
            (PdfObject? nodeObj, string? parentType) = stack.Pop();
            if (context.Resolve(nodeObj) is not PdfDictionary node)
                continue; // an integer MCID leaf or a non-dictionary
            if (node.IsIndirect && !visited.Add(node.ObjectNumber))
                continue;
            if (!IsStructureElement(context, node))
                continue; // an /MCR or /OBJR reference, or a node without /S

            string? myType = StandardType(context, node);

            var kidTypes = new List<string?>();
            foreach (PdfObject kidObj in KidObjects(context, node))
            {
                stack.Push((kidObj, myType));
                if (context.Resolve(kidObj) is PdfDictionary kid && IsStructureElement(context, kid))
                    kidTypes.Add(StandardType(context, kid));
            }

            yield return new StructureNode(node, myType, parentType, kidTypes);
        }
    }

    /// <summary>
    /// Maps each annotation object (by object number) referenced by an <c>/OBJR</c> in the structure tree to
    /// the structure element that <em>directly</em> contains that <c>/OBJR</c> — the annotation's enclosing
    /// structure element. An annotation that no <c>/OBJR</c> references is simply absent from the map (so a
    /// caller can distinguish "untagged" from "tagged under element E"). Backs the PDF/UA annotation nesting
    /// and alternate-description rules (7.18.1/.4/.5/.8).
    /// </summary>
    public static IReadOnlyDictionary<int, PdfDictionary> AnnotationParentElements(ConformanceContext context)
    {
        var map = new Dictionary<int, PdfDictionary>();
        foreach (StructureNode node in Nodes(context))
        {
            foreach (PdfObject kidObj in KidObjects(context, node.Element))
            {
                if (context.Resolve(kidObj) is not PdfDictionary kid) continue;
                if (context.ResolveName(kid.Get("Type")) != "OBJR") continue;
                if (context.Resolve(kid.Get("Obj")) is { IsIndirect: true } annot)
                    map[annot.ObjectNumber] = node.Element; // last container wins if referenced twice
            }
        }
        return map;
    }

    /// <summary>The resolved structure-element children of <paramref name="element"/> (its <c>/K</c> entries
    /// that are themselves structure elements — integer MCIDs and <c>/MCR</c>/<c>/OBJR</c> references are
    /// excluded), in order. Used by the table-grid rule to walk Table → rows → cells.</summary>
    public static IEnumerable<PdfDictionary> ChildElements(ConformanceContext context, PdfDictionary element)
    {
        foreach (PdfObject kidObj in KidObjects(context, element))
            if (context.Resolve(kidObj) is PdfDictionary kid && IsStructureElement(context, kid))
                yield return kid;
    }

    // A structure element carries an /S type and is not a marked-content (/MCR) or object (/OBJR) reference.
    private static bool IsStructureElement(ConformanceContext context, PdfDictionary node)
    {
        if (context.ResolveName(node.Get("Type")) is "MCR" or "OBJR")
            return false;
        return node.Get("S") is not null;
    }

    // The child objects listed in a node's /K, unresolved (a single object, an array, or an integer MCID).
    private static IEnumerable<PdfObject> KidObjects(ConformanceContext context, PdfDictionary node)
    {
        switch (context.Resolve(node.Get("K")))
        {
            case PdfArray kids:
                foreach (PdfObject kid in kids)
                    yield return kid;
                break;
            case { } single:
                yield return single;
                break;
        }
    }
}
