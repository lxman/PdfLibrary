using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
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

        var pages = new List<(PdfDictionary dict, PdfIndirectReference reference)>();
        Collect(doc, root, pages);

        foreach ((PdfDictionary dict, _) in pages)
            MaterializeInheritance(doc, dict);

        PdfIndirectReference rootRef = PageTreeOps.RootRef(doc);
        var kids = new PdfArray();
        foreach ((PdfDictionary dict, PdfIndirectReference reference) in pages)
        {
            dict[new PdfName("Parent")] = rootRef;
            kids.Add(reference);
        }
        root[new PdfName("Kids")] = kids;
        root[new PdfName("Count")] = new PdfInteger(pages.Count);
    }

    private static void Collect(PdfDocument doc, PdfDictionary node, List<(PdfDictionary, PdfIndirectReference)> acc)
    {
        if (!node.TryGetValue(new PdfName("Kids"), out PdfObject kidsObj) || kidsObj is not PdfArray kids) return;
        foreach (PdfObject kid in kids)
        {
            PdfDictionary? dict;
            PdfIndirectReference reference;
            if (kid is PdfIndirectReference r)
            {
                dict = doc.GetObject(r.ObjectNumber) as PdfDictionary;
                reference = r;
            }
            else if (kid is PdfDictionary direct)
            {
                dict = direct;
                reference = doc.RegisterObject(direct); // promote a direct leaf to indirect
            }
            else continue;
            if (dict is null) continue;

            string type = dict.TryGetValue(PdfName.TypeName, out PdfObject t) && t is PdfName n ? n.Value : "";
            if (type == "Pages") Collect(doc, dict, acc);
            else acc.Add((dict, reference));
        }
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
