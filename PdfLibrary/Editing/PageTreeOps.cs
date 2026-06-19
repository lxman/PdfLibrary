using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing;

/// <summary>Read and splice helpers over a (flattened) page tree.</summary>
internal static class PageTreeOps
{
    internal static PdfArray Kids(PdfDocument doc)
    {
        PdfDictionary root = doc.PageTreeRootDictionary
            ?? throw new InvalidOperationException("Document has no page tree root.");
        if (root.TryGetValue(new PdfName("Kids"), out PdfObject obj) && obj is PdfArray kids)
            return kids;
        var empty = new PdfArray();
        root[new PdfName("Kids")] = empty;
        return empty;
    }

    internal static IReadOnlyList<PdfDictionary> PageDicts(PdfDocument doc)
    {
        var result = new List<PdfDictionary>();
        foreach (PdfObject kid in Kids(doc))
        {
            PdfObject? resolved = kid is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : kid;
            if (resolved is PdfDictionary d) result.Add(d);
        }
        return result;
    }

    internal static void Move(PdfDocument doc, int from, int to)
    {
        PdfArray kids = Kids(doc);
        if (from < 0 || from >= kids.Count) throw new ArgumentOutOfRangeException(nameof(from));
        if (to < 0 || to >= kids.Count) throw new ArgumentOutOfRangeException(nameof(to));
        PdfObject item = kids[from];
        kids.RemoveAt(from);
        kids.Insert(to, item);
    }

    internal static PdfDictionary RemoveAt(PdfDocument doc, int index)
    {
        PdfArray kids = Kids(doc);
        if (index < 0 || index >= kids.Count) throw new ArgumentOutOfRangeException(nameof(index));
        PdfObject kid = kids[index];
        PdfDictionary pageDict = (kid is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : kid) as PdfDictionary
            ?? throw new InvalidOperationException("Page kid is not a dictionary.");
        kids.RemoveAt(index);
        SetCount(doc, kids.Count);
        return pageDict;
    }

    internal static void SetCount(PdfDocument doc, int count)
    {
        PdfDictionary root = doc.PageTreeRootDictionary
            ?? throw new InvalidOperationException("Document has no page tree root.");
        root[new PdfName("Count")] = new PdfInteger(count);
    }

    /// <summary>The catalog's /Pages indirect reference (promotes a direct /Pages dict to indirect if needed).</summary>
    internal static PdfIndirectReference RootRef(PdfDocument doc)
    {
        PdfDictionary catalog = doc.CatalogDictionary
            ?? throw new InvalidOperationException("Document has no catalog.");
        if (!catalog.TryGetValue(new PdfName("Pages"), out PdfObject pagesObj))
            throw new InvalidOperationException("Catalog has no /Pages.");
        if (pagesObj is PdfIndirectReference reference)
            return reference;
        if (pagesObj is PdfDictionary pagesDict)
        {
            PdfIndirectReference newRef = doc.RegisterObject(pagesDict);
            catalog[new PdfName("Pages")] = newRef;
            return newRef;
        }
        throw new InvalidOperationException("Catalog /Pages is neither a reference nor a dictionary.");
    }
}
