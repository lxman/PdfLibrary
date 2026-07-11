using System;
using System.Collections.Generic;
using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Document;

/// <summary>
/// A read-only view of a document's Tagged-PDF logical structure (ISO 32000-1, 14.7) — the accessibility
/// tag tree. Built with <see cref="PdfDocument.GetTagTree"/>. Intended for inspection (an accessibility
/// panel, a reading-order view); it does not modify the document.
/// </summary>
public sealed class TagTree
{
    internal TagTree(bool isTagged, IReadOnlyList<TagNode> roots)
    {
        IsTagged = isTagged;
        Roots = roots;
    }

    /// <summary>True when the document declares itself a Tagged PDF: the catalog has a <c>/StructTreeRoot</c>
    /// and a <c>/MarkInfo</c> dictionary whose <c>/Marked</c> flag is true. When false, <see cref="Roots"/>
    /// may still be non-empty (a structure tree is present but the file is not marked as tagged).</summary>
    public bool IsTagged { get; }

    /// <summary>The top-level structure elements, in document order. Empty when the document has no
    /// structure tree.</summary>
    public IReadOnlyList<TagNode> Roots { get; }
}

/// <summary>
/// A node in the logical structure tree — one structure element, with its role-mapped type, its accessibility
/// attributes, the page it sits on, its own text content, and its child elements. Read-only.
/// </summary>
public sealed class TagNode
{
    internal TagNode(string? type, string? rawType, bool isStandard, string? alt, string? actualText,
        string? expansion, string? language, string? title, int? pageIndex,
        IReadOnlyList<TagNode> children, string text)
    {
        Type = type;
        RawType = rawType;
        IsStandard = isStandard;
        Alt = alt;
        ActualText = actualText;
        Expansion = expansion;
        Language = language;
        Title = title;
        PageIndex = pageIndex;
        Children = children;
        Text = text;
    }

    /// <summary>The structure type resolved through the tree's <c>/RoleMap</c> to a standard ISO 32000 type
    /// (e.g. "H1", "P", "Figure", "Table"), or the raw type when it maps to nothing standard.</summary>
    public string? Type { get; }

    /// <summary>The element's literal <c>/S</c> structure type before role mapping (e.g. a custom
    /// "Heading1").</summary>
    public string? RawType { get; }

    /// <summary>Whether <see cref="Type"/> is one of the ISO 32000-1 standard structure types.</summary>
    public bool IsStandard { get; }

    /// <summary>The element's <c>/Alt</c> alternate description, or null when absent.</summary>
    public string? Alt { get; }

    /// <summary>The element's <c>/ActualText</c> replacement text, or null when absent.</summary>
    public string? ActualText { get; }

    /// <summary>The element's <c>/E</c> expansion of an abbreviation/acronym, or null when absent.</summary>
    public string? Expansion { get; }

    /// <summary>The element's <c>/Lang</c> natural-language identifier, or null when absent.</summary>
    public string? Language { get; }

    /// <summary>The element's <c>/T</c> human-readable title, or null when absent.</summary>
    public string? Title { get; }

    /// <summary>The 0-based index of the page this element's content is on (from <c>/Pg</c>, own or
    /// inherited), or null when it cannot be determined.</summary>
    public int? PageIndex { get; }

    /// <summary>This element's <em>own</em> text content — the text of the marked-content sequences it
    /// references directly (its <c>/K</c> MCIDs and <c>/MCR</c> references). Text belonging to child
    /// elements is on those children, not here, so a grouping element (Document, Sect…) is usually empty.</summary>
    public string Text { get; }

    /// <summary>The element's child structure elements, in document order.</summary>
    public IReadOnlyList<TagNode> Children { get; }

    public override string ToString() =>
        $"<{Type ?? RawType ?? "?"}>{(Text.Length > 0 ? $" \"{Truncate(Text)}\"" : "")}";

    private static string Truncate(string s) => s.Length <= 40 ? s : s[..40] + "…";
}

/// <summary>Builds a <see cref="TagTree"/> from a document's <c>/StructTreeRoot</c>, resolving per-element
/// text through the page marked-content (MCID) mapping. Internal — the object model it reads is internal.</summary>
internal static class TagTreeBuilder
{
    public static TagTree Build(PdfDocument document)
    {
        PdfDictionary? root = LogicalStructure.StructTreeRootDictionary(document);
        if (root is null)
            return new TagTree(false, []);

        List<PdfPage> pages = document.GetPages();
        var pageIndexByObject = new Dictionary<int, int>();
        for (int i = 0; i < pages.Count; i++)
            if (pages[i].Dictionary.IsIndirect)
                pageIndexByObject[pages[i].Dictionary.ObjectNumber] = i;

        // Per-page MCID → text, built lazily (only for pages a structure element actually references).
        var mcidText = new Dictionary<int, string>?[pages.Count];

        var visited = new HashSet<int>();
        var roots = new List<TagNode>();
        foreach (PdfDictionary element in LogicalStructure.ChildElements(document, root))
            roots.Add(BuildNode(document, element, null, visited, pageIndexByObject, pages, mcidText));

        return new TagTree(IsMarked(document), roots);
    }

    private static TagNode BuildNode(
        PdfDocument document, PdfDictionary element, PdfDictionary? inheritedPage, HashSet<int> visited,
        IReadOnlyDictionary<int, int> pageIndexByObject, List<PdfPage> pages, Dictionary<int, string>?[] mcidText)
    {
        bool recurse = !element.IsIndirect || visited.Add(element.ObjectNumber); // cycle guard

        PdfDictionary? page = Resolve(document, element.Get("Pg")) as PdfDictionary ?? inheritedPage;
        int? pageIndex = PageIndexOf(page, pageIndexByObject);

        string? type = LogicalStructure.StandardType(document, element);

        var text = new StringBuilder();
        foreach ((int mcid, PdfDictionary? mcPage) in LogicalStructure.ContentMcids(document, element, page))
        {
            int? mcPageIndex = PageIndexOf(mcPage, pageIndexByObject) ?? pageIndex;
            if (mcPageIndex is int p && McidTextOf(p, mcid, pages, mcidText) is { Length: > 0 } t)
            {
                if (text.Length > 0) text.Append(' ');
                text.Append(t);
            }
        }

        var children = new List<TagNode>();
        if (recurse)
            foreach (PdfDictionary child in LogicalStructure.ChildElements(document, element))
                children.Add(BuildNode(document, child, page, visited, pageIndexByObject, pages, mcidText));

        return new TagNode(
            type, ResolveName(document, element.Get("S")), LogicalStructure.IsStandardType(type),
            TextValue(document, element.Get("Alt")), TextValue(document, element.Get("ActualText")),
            TextValue(document, element.Get("E")), TextValue(document, element.Get("Lang")),
            TextValue(document, element.Get("T")), pageIndex, children, text.ToString());
    }

    private static int? PageIndexOf(PdfDictionary? page, IReadOnlyDictionary<int, int> map) =>
        page is { IsIndirect: true } p && map.TryGetValue(p.ObjectNumber, out int i) ? i : null;

    /// <summary>The text of one marked-content id on a page, built (and cached) from the page's text
    /// fragments: the assembled-page substring spanning that MCID's fragments, so word separators are kept.</summary>
    private static string? McidTextOf(int pageIndex, int mcid, List<PdfPage> pages, Dictionary<int, string>?[] cache)
    {
        cache[pageIndex] ??= BuildMcidText(pages[pageIndex]);
        return cache[pageIndex]!.GetValueOrDefault(mcid);
    }

    private static Dictionary<int, string> BuildMcidText(PdfPage page)
    {
        (string assembled, List<Content.TextFragment> fragments) = page.ExtractTextWithFragments();

        // Group each MCID's own fragments (an MCID's fragments are not always contiguous in the assembled
        // text, so a min..max substring would swallow other MCIDs' text — concatenate this MCID's fragments
        // only, restoring a single separator between them from the assembled text between their offsets).
        var byMcid = new Dictionary<int, List<Content.TextFragment>>();
        foreach (Content.TextFragment f in fragments)
        {
            if (f.Mcid is not int m)
                continue;
            if (!byMcid.TryGetValue(m, out List<Content.TextFragment>? list))
                byMcid[m] = list = [];
            list.Add(f);
        }

        var result = new Dictionary<int, string>(byMcid.Count);
        foreach ((int mcid, List<Content.TextFragment> frags) in byMcid)
        {
            frags.Sort((a, b) => a.TextOffset.CompareTo(b.TextOffset));
            var sb = new StringBuilder();
            int prevEnd = -1;
            foreach (Content.TextFragment f in frags)
            {
                if (prevEnd >= 0 && f.TextOffset > prevEnd)
                {
                    int gapEnd = Math.Clamp(f.TextOffset, prevEnd, assembled.Length);
                    string between = assembled[Math.Clamp(prevEnd, 0, assembled.Length)..gapEnd];
                    sb.Append(between.Contains('\n') ? '\n' : ' ');
                }
                sb.Append(f.Text);
                prevEnd = f.TextOffset + f.Text.Length;
            }
            result[mcid] = sb.ToString().Trim();
        }
        return result;
    }

    private static bool IsMarked(PdfDocument document)
    {
        PdfDictionary? catalog = document.GetCatalog()?.Dictionary;
        return Resolve(document, catalog?.Get("MarkInfo")) is PdfDictionary markInfo
               && Resolve(document, markInfo.Get("Marked")) is PdfBoolean { Value: true };
    }

    private static string? TextValue(PdfDocument document, PdfObject? obj) =>
        Resolve(document, obj) is PdfString s ? s.GetText() : null;

    private static PdfObject? Resolve(PdfDocument document, PdfObject? obj) =>
        obj is PdfIndirectReference reference ? document.ResolveReference(reference) : obj;

    private static string? ResolveName(PdfDocument document, PdfObject? obj) =>
        (Resolve(document, obj) as PdfName)?.Value;
}
