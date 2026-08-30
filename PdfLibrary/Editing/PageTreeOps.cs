using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing;

/// <summary>Read and splice helpers over a page tree.</summary>
internal static class PageTreeOps
{
    /// <summary>Reads the root /Kids array without changing the document.</summary>
    internal static PdfArray Kids(PdfDocument doc)
    {
        PdfDictionary? root = doc.PageTreeRootDictionary;
        if (root is not null
            && root.TryGetValue(new PdfName("Kids"), out PdfObject obj)
            && obj is PdfArray kids)
            return kids;
        return new PdfArray();
    }

    internal static IReadOnlyList<PdfDictionary> PageDicts(PdfDocument doc)
    {
        PdfDictionary? root = doc.PageTreeRootDictionary;
        return root is null
            ? []
            : [.. PdfPageTreeWalker.Collect(root, doc).Select(leaf => leaf.Dictionary)];
    }

    internal static void Move(PdfDocument doc, int from, int to)
    {
        int count = PageDicts(doc).Count;
        if (from < 0 || from >= count) throw new ArgumentOutOfRangeException(nameof(from));
        if (to < 0 || to >= count) throw new ArgumentOutOfRangeException(nameof(to));
        PdfArray kids = WritableKids(doc);
        PdfObject item = kids[from];
        kids.RemoveAt(from);
        kids.Insert(to, item);
    }

    internal static PdfDictionary RemoveAt(PdfDocument doc, int index)
    {
        int count = PageDicts(doc).Count;
        if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        PdfArray kids = WritableKids(doc);
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

    internal static void InsertPageRef(PdfDocument doc, PdfIndirectReference pageRef, int at)
    {
        int count = PageDicts(doc).Count;
        if (at < 0 || at > count) throw new ArgumentOutOfRangeException(nameof(at));
        PdfArray kids = WritableKids(doc);
        kids.Insert(at, pageRef);
        if (doc.GetObject(pageRef.ObjectNumber) is PdfDictionary page)
            page[new PdfName("Parent")] = RootRef(doc);
        SetCount(doc, kids.Count);
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

    private static PdfArray WritableKids(PdfDocument doc)
    {
        PageTreeNormalizer.Normalize(doc);
        PdfDictionary root = doc.PageTreeRootDictionary
            ?? throw new InvalidOperationException("Document has no page tree root.");
        if (root.Get("Kids") is PdfArray kids)
            return kids;

        var created = new PdfArray();
        root[new PdfName("Kids")] = created;
        return created;
    }
}
