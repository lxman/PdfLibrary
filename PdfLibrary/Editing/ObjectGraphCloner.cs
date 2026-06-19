using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing;

/// <summary>
/// Deep-copies a page's reachable object subgraph from <paramref name="source"/> into
/// <paramref name="target"/>, allocating fresh object numbers and rewriting indirect references
/// through a visited-map so shared objects copy once and cycles terminate.
/// </summary>
internal static class ObjectGraphCloner
{
    public static PdfIndirectReference CloneInto(PdfDocument target, PdfDocument source, PdfDictionary sourcePageDict)
    {
        var map = new Dictionary<int, PdfIndirectReference>();

        PdfIndirectReference pageRef = target.RegisterObject(new PdfDictionary());
        if (sourcePageDict.IsIndirect)
            map[sourcePageDict.ObjectNumber] = pageRef;

        PdfDictionary pageCopy = ResolveInheritedPage(source, sourcePageDict);
        pageCopy.Remove(new PdfName("Parent"));
        var clonedPage = (PdfDictionary)CloneValue(target, source, pageCopy, map);
        target.ReplaceObject(pageRef.ObjectNumber, clonedPage);
        return pageRef;
    }

    /// <summary>Clones a single value into the target with a fresh visited-map.</summary>
    public static PdfObject CloneValue(PdfDocument target, PdfDocument source, PdfObject value) =>
        CloneValue(target, source, value, new Dictionary<int, PdfIndirectReference>());

    private static PdfObject CloneValue(PdfDocument target, PdfDocument source, PdfObject obj,
        Dictionary<int, PdfIndirectReference> map)
    {
        if (obj is PdfIndirectReference reference)
        {
            if (map.TryGetValue(reference.ObjectNumber, out PdfIndirectReference? existing))
                return existing;
            PdfIndirectReference placeholder = target.RegisterObject(new PdfDictionary());
            map[reference.ObjectNumber] = placeholder;
            PdfObject? resolved = source.GetObject(reference.ObjectNumber);
            PdfObject content = resolved is null ? PdfNull.Instance : CloneContent(target, source, resolved, map);
            target.ReplaceObject(placeholder.ObjectNumber, content);
            return placeholder;
        }
        return CloneContent(target, source, obj, map);
    }

    private static PdfObject CloneContent(PdfDocument target, PdfDocument source, PdfObject obj,
        Dictionary<int, PdfIndirectReference> map)
    {
        switch (obj)
        {
            case PdfDictionary dict:
            {
                var clone = new PdfDictionary();
                foreach (KeyValuePair<PdfName, PdfObject> kv in dict)
                    clone[kv.Key] = CloneValue(target, source, kv.Value, map);
                return clone;
            }
            case PdfArray array:
            {
                var clone = new PdfArray();
                foreach (PdfObject item in array)
                    clone.Add(CloneValue(target, source, item, map));
                return clone;
            }
            case PdfStream stream:
            {
                var dictClone = new PdfDictionary();
                foreach (KeyValuePair<PdfName, PdfObject> kv in stream.Dictionary)
                    dictClone[kv.Key] = CloneValue(target, source, kv.Value, map);
                byte[] raw = source.Decryptor is not null && stream.IsIndirect
                    ? source.Decryptor.Decrypt(stream.Data, stream.ObjectNumber, stream.GenerationNumber)
                    : stream.Data;
                return new PdfStream(dictClone, raw);
            }
            default:
                return obj;
        }
    }

    private static PdfDictionary ResolveInheritedPage(PdfDocument source, PdfDictionary page)
    {
        var copy = new PdfDictionary();
        foreach (KeyValuePair<PdfName, PdfObject> kv in page) copy[kv.Key] = kv.Value;
        foreach (string key in new[] { "Resources", "MediaBox", "CropBox", "Rotate" })
        {
            var name = new PdfName(key);
            if (copy.ContainsKey(name)) continue;
            PdfObject? inherited = FindInherited(source, page, name);
            if (inherited is not null) copy[name] = inherited;
        }
        return copy;
    }

    private static PdfObject? FindInherited(PdfDocument source, PdfDictionary page, PdfName key)
    {
        PdfObject? parentObj = page.TryGetValue(new PdfName("Parent"), out PdfObject p) ? p : null;
        var guard = 0;
        while (parentObj is not null && guard++ < 64)
        {
            PdfObject? resolved = parentObj is PdfIndirectReference r ? source.GetObject(r.ObjectNumber) : parentObj;
            if (resolved is not PdfDictionary node) break;
            if (node.TryGetValue(key, out PdfObject val)) return val;
            parentObj = node.TryGetValue(new PdfName("Parent"), out PdfObject pp) ? pp : null;
        }
        return null;
    }
}
