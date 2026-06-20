using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing;

/// <summary>
/// A live view of the document's page-label ranges (<c>/Catalog /PageLabels</c> number tree).
/// Reads/writes a single flat <c>/Nums</c> node, kept sorted by start index.
/// </summary>
/// <remarks>
/// Page-label ranges are keyed to physical page positions and are NOT adjusted automatically
/// when pages are inserted, removed, moved, or imported. After rearranging pages, call
/// <see cref="Set"/>/<see cref="Remove"/> to renumber as needed.
/// </remarks>
public sealed class PdfPageLabels
{
    private readonly PdfDocument _document;

    internal PdfPageLabels(PdfDocument document) => _document = document;

    /// <summary>All ranges, sorted by start page index.</summary>
    public IReadOnlyList<PdfPageLabelRange> Ranges =>
        PageLabelTree.Read(_document).OrderBy(r => r.StartPageIndex).ToList();

    /// <summary>
    /// Defines (or replaces) the labeling range that starts at <paramref name="startIndex"/>.
    /// </summary>
    public void Set(int startIndex, PdfPageLabelStyle style, string? prefix = null, int start = 1)
    {
        if (startIndex < 0) throw new ArgumentOutOfRangeException(nameof(startIndex));

        List<PdfPageLabelRange> ranges = PageLabelTree.Read(_document);
        ranges.RemoveAll(r => r.StartPageIndex == startIndex);
        ranges.Add(PageLabelTree.MakeRange(startIndex, style, prefix, start));
        PageLabelTree.Write(_document, ranges);
    }

    /// <summary>The range covering <paramref name="index"/> (the latest range starting at or before it), or null.</summary>
    public PdfPageLabelRange? Get(int index)
    {
        PdfPageLabelRange? best = null;
        foreach (PdfPageLabelRange r in PageLabelTree.Read(_document))
            if (r.StartPageIndex <= index && (best is null || r.StartPageIndex > best.StartPageIndex))
                best = r;
        return best;
    }

    /// <summary>Removes the range starting exactly at <paramref name="startIndex"/>. Returns false if none.</summary>
    public bool Remove(int startIndex)
    {
        List<PdfPageLabelRange> ranges = PageLabelTree.Read(_document);
        int removed = ranges.RemoveAll(r => r.StartPageIndex == startIndex);
        if (removed == 0) return false;
        if (ranges.Count == 0) PageLabelTree.RemoveTree(_document);
        else PageLabelTree.Write(_document, ranges);
        return true;
    }

    /// <summary>Removes the entire <c>/PageLabels</c> tree.</summary>
    public void Clear() => PageLabelTree.RemoveTree(_document);
}

/// <summary>
/// Shared read/write helpers for the <c>/Catalog /PageLabels</c> number tree.
/// Mirrors <c>PdfDocumentWriter.WritePageLabelsInline</c>'s D/R/r/A/a style mapping.
/// </summary>
internal static class PageLabelTree
{
    internal static PdfPageLabelRange MakeRange(int startIndex, PdfPageLabelStyle style, string? prefix, int start)
    {
        var range = new PdfPageLabelRange(startIndex) { Style = style };
        if (!string.IsNullOrEmpty(prefix)) range.Prefix = prefix;
        range.StartNumber = start;
        return range;
    }

    /// <summary>Reads all label ranges from the flat /Nums node (unsorted as stored).</summary>
    internal static List<PdfPageLabelRange> Read(PdfDocument doc)
    {
        var result = new List<PdfPageLabelRange>();
        if (Resolve(doc, doc.CatalogDictionary?.Get(new PdfName("PageLabels"))) is not PdfDictionary pl)
            return result;
        if (Resolve(doc, pl.Get(new PdfName("Nums"))) is not PdfArray nums) return result;

        for (var i = 0; i + 1 < nums.Count; i += 2)
        {
            if (Resolve(doc, nums[i]) is not PdfInteger key) continue;
            if (Resolve(doc, nums[i + 1]) is not PdfDictionary dict) continue;

            PdfPageLabelStyle style = ReadStyle(dict.Get(new PdfName("S")));
            string? prefix = Resolve(doc, dict.Get(new PdfName("P"))) is PdfString p ? p.Value : null;
            int start = Resolve(doc, dict.Get(new PdfName("St"))) is PdfInteger st ? st.Value : 1;

            result.Add(MakeRange(key.Value, style, prefix, start));
        }
        return result;
    }

    /// <summary>Writes the ranges into a flat /Nums node (sorted, de-duped last-wins), creating the dict on demand.</summary>
    internal static void Write(PdfDocument doc, IReadOnlyList<PdfPageLabelRange> ranges)
    {
        PdfDictionary catalog = doc.CatalogDictionary
            ?? throw new InvalidOperationException("Document has no catalog.");

        // De-dup by start index (last wins), then sort.
        var byStart = new Dictionary<int, PdfPageLabelRange>();
        foreach (PdfPageLabelRange r in ranges)
            byStart[r.StartPageIndex] = r;

        var nums = new PdfArray();
        foreach (PdfPageLabelRange r in byStart.Values.OrderBy(r => r.StartPageIndex))
        {
            nums.Add(new PdfInteger(r.StartPageIndex));
            nums.Add(BuildEntry(r));
        }

        PdfDictionary pl;
        if (Resolve(doc, catalog.Get(new PdfName("PageLabels"))) is PdfDictionary existing)
            pl = existing;
        else
        {
            pl = new PdfDictionary();
            catalog[new PdfName("PageLabels")] = doc.RegisterObject(pl);
        }
        pl[new PdfName("Nums")] = nums;
    }

    internal static void RemoveTree(PdfDocument doc)
    {
        PdfDictionary? catalog = doc.CatalogDictionary;
        if (catalog is null) return;
        if (catalog.Get(new PdfName("PageLabels")) is PdfIndirectReference r)
            doc.RemoveObject(r.ObjectNumber);
        catalog.Remove(new PdfName("PageLabels"));
    }

    private static PdfDictionary BuildEntry(PdfPageLabelRange r)
    {
        var entry = new PdfDictionary();
        if (r.Style != PdfPageLabelStyle.None)
            entry[new PdfName("S")] = new PdfName(StyleCode(r.Style));
        if (!string.IsNullOrEmpty(r.Prefix))
            entry[new PdfName("P")] = new PdfString(r.Prefix);
        if (r.StartNumber != 1)
            entry[new PdfName("St")] = new PdfInteger(r.StartNumber);
        return entry;
    }

    private static string StyleCode(PdfPageLabelStyle style) => style switch
    {
        PdfPageLabelStyle.Decimal => "D",
        PdfPageLabelStyle.UppercaseRoman => "R",
        PdfPageLabelStyle.LowercaseRoman => "r",
        PdfPageLabelStyle.UppercaseLetters => "A",
        PdfPageLabelStyle.LowercaseLetters => "a",
        _ => "D"
    };

    private static PdfPageLabelStyle ReadStyle(PdfObject? s) =>
        s is PdfName name
            ? name.Value switch
            {
                "D" => PdfPageLabelStyle.Decimal,
                "R" => PdfPageLabelStyle.UppercaseRoman,
                "r" => PdfPageLabelStyle.LowercaseRoman,
                "A" => PdfPageLabelStyle.UppercaseLetters,
                "a" => PdfPageLabelStyle.LowercaseLetters,
                _ => PdfPageLabelStyle.Decimal
            }
            : PdfPageLabelStyle.None;

    private static PdfObject? Resolve(PdfDocument doc, PdfObject? obj) =>
        obj is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : obj;
}
