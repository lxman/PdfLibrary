using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing;

/// <summary>
/// Flattens a page tree to a single-level tree and materializes the four inheritable page
/// attributes (Resources, MediaBox, CropBox, Rotate) onto each page before intermediate nodes
/// become unreachable. Idempotent.
/// </summary>
internal static class PageTreeNormalizer
{
    private static readonly string[] Inheritable = ["Resources", "MediaBox", "CropBox", "Rotate"];

    public static void Normalize(PdfDocument doc)
    {
        PdfDictionary? root = doc.PageTreeRootDictionary;
        if (root is null) return;

        IReadOnlyList<PdfPageTreeLeaf> pages = PdfPageTreeWalker.Collect(root, doc);

        foreach (PdfPageTreeLeaf page in pages)
            MaterializeInheritance(doc, page.Dictionary);

        PdfIndirectReference rootRef = PageTreeOps.RootRef(doc);
        var kids = new PdfArray();
        foreach (PdfPageTreeLeaf page in pages)
        {
            PdfDictionary dict = page.Dictionary;
            PdfIndirectReference reference = page.Reference
                ?? (dict.IsIndirect
                    ? new PdfIndirectReference(dict.ObjectNumber, dict.GenerationNumber)
                    : doc.RegisterObject(dict));
            dict[new PdfName("Parent")] = rootRef;
            kids.Add(reference);
        }
        root[new PdfName("Kids")] = kids;
        root[new PdfName("Count")] = new PdfInteger(pages.Count);
    }

    private static void MaterializeInheritance(PdfDocument doc, PdfDictionary page)
    {
        foreach (string key in Inheritable)
        {
            var name = new PdfName(key);
            if (page.ContainsKey(name)) continue;
            PdfObject? value = FindInherited(doc, page, name);
            if (value is not null) page[name] = value;
        }
    }

    private static PdfObject? FindInherited(PdfDocument doc, PdfDictionary page, PdfName key)
    {
        PdfObject? parentObj = page.TryGetValue(new PdfName("Parent"), out PdfObject p) ? p : null;
        var guard = 0;
        while (parentObj is not null && guard++ < 64)
        {
            PdfObject? resolved = parentObj is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : parentObj;
            if (resolved is not PdfDictionary node) break;
            if (node.TryGetValue(key, out PdfObject val)) return val;
            parentObj = node.TryGetValue(new PdfName("Parent"), out PdfObject pp) ? pp : null;
        }
        return null;
    }
}
