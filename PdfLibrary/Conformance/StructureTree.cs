using System.Collections.Generic;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance;

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
    /// <c>/S</c> when it maps to nothing. Chained role mappings are followed with a cycle guard.</summary>
    public static string? StandardType(ConformanceContext context, PdfDictionary element)
    {
        string? type = context.ResolveName(element.Get("S"));
        if (type is null)
            return null;

        if (context.Resolve(RoleMapOf(context)) is not PdfDictionary roleMap)
            return type;

        var seen = new HashSet<string>();
        while (seen.Add(type) && context.ResolveName(roleMap.Get(type)) is { } mapped)
            type = mapped;
        return type;
    }

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
}
