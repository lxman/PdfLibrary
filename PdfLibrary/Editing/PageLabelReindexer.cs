using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing;

/// <summary>
/// Shifts page-label range boundaries to track structural page edits.
/// Every entry point is a no-op when <c>/PageLabels</c> is absent (zero behavior
/// change for unlabeled documents).
/// </summary>
internal static class PageLabelReindexer
{
    /// <summary>A page was inserted at <paramref name="at"/>: ranges with start &gt;= at shift +1.</summary>
    public static void OnPageInserted(PdfDocument doc, int at)
    {
        if (!HasPageLabels(doc)) return;

        List<PdfPageLabelRange> ranges = PageLabelTree.Read(doc);
        var shifted = new List<PdfPageLabelRange>(ranges.Count);
        foreach (PdfPageLabelRange r in ranges)
        {
            int start = r.StartPageIndex >= at ? r.StartPageIndex + 1 : r.StartPageIndex;
            shifted.Add(Clone(r, start));
        }
        WriteOrRemove(doc, shifted);
    }

    /// <summary>
    /// A page at <paramref name="removedIndex"/> was removed: a range starting exactly there is
    /// dropped (the next range already covers from its start); ranges with start &gt; idx shift -1.
    /// </summary>
    public static void OnPageRemoved(PdfDocument doc, int removedIndex)
    {
        if (!HasPageLabels(doc)) return;

        List<PdfPageLabelRange> ranges = PageLabelTree.Read(doc);
        var shifted = new List<PdfPageLabelRange>(ranges.Count);
        foreach (PdfPageLabelRange r in ranges)
        {
            if (r.StartPageIndex == removedIndex) continue; // dropped
            int start = r.StartPageIndex > removedIndex ? r.StartPageIndex - 1 : r.StartPageIndex;
            if (start < 0) start = 0;
            shifted.Add(Clone(r, start));
        }
        WriteOrRemove(doc, shifted);
    }

    /// <summary>A page moved from <paramref name="from"/> to <paramref name="to"/>: remove(from)+insert(to).</summary>
    public static void OnPageMoved(PdfDocument doc, int from, int to)
    {
        if (!HasPageLabels(doc)) return;
        OnPageRemoved(doc, from);
        OnPageInserted(doc, to);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static bool HasPageLabels(PdfDocument doc)
    {
        PdfObject? pl = doc.CatalogDictionary?.Get(new PdfName("PageLabels"));
        if (pl is null) return false;
        return Resolve(doc, pl) is PdfDictionary;
    }

    private static PdfPageLabelRange Clone(PdfPageLabelRange r, int newStart) =>
        PageLabelTree.MakeRange(newStart, r.Style, r.Prefix, r.StartNumber);

    private static void WriteOrRemove(PdfDocument doc, List<PdfPageLabelRange> ranges)
    {
        if (ranges.Count == 0) PageLabelTree.RemoveTree(doc);
        else PageLabelTree.Write(doc, ranges); // sorted + de-duped (last wins) inside Write
    }

    private static PdfObject? Resolve(PdfDocument doc, PdfObject? obj) =>
        obj is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : obj;
}
