using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing;

/// <summary>
/// Deep-copies a page's reachable object subgraph from <c>source</c> into
/// <c>target</c>, allocating fresh object numbers and rewriting indirect references
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

        // Field-tree-aware cloning: a widget's /Parent edge climbs into the form's field
        // hierarchy, and on XFA-style forms (one shared root, e.g. the IRS W-2) the root's
        // /Kids reference EVERY field of EVERY page — a naive graph walk imports the whole
        // field forest, and each stray widget's /P then drags in orphan clones of the source
        // pages themselves. So: skip /Parent on this page's widgets during the graph clone,
        // then rebuild just the ancestor SPINE with /Kids filtered to the cloned children.
        List<(int WidgetObjNum, PdfIndirectReference SrcParent)> deferredParents =
            CollectWidgetParents(source, pageCopy);
        var skipParentOn = new HashSet<int>(deferredParents.Select(d => d.WidgetObjNum));

        var clonedPage = (PdfDictionary)CloneValue(target, source, pageCopy, map, skipParentOn);
        target.ReplaceObject(pageRef.ObjectNumber, clonedPage);
        RebuildFieldSpines(target, source, map, deferredParents);
        return pageRef;
    }

    /// <summary>Clones a single value into the target with a fresh visited-map.</summary>
    public static PdfObject CloneValue(PdfDocument target, PdfDocument source, PdfObject value) =>
        CloneValue(target, source, value, new Dictionary<int, PdfIndirectReference>(), null);

    private static PdfObject CloneValue(PdfDocument target, PdfDocument source, PdfObject obj,
        Dictionary<int, PdfIndirectReference> map, HashSet<int>? skipParentOn)
    {
        if (obj is PdfIndirectReference reference)
        {
            if (map.TryGetValue(reference.ObjectNumber, out PdfIndirectReference? existing))
                return existing;
            PdfIndirectReference placeholder = target.RegisterObject(new PdfDictionary());
            map[reference.ObjectNumber] = placeholder;
            PdfObject? resolved = source.GetObject(reference.ObjectNumber);
            bool skipParent = skipParentOn?.Contains(reference.ObjectNumber) == true;
            PdfObject content = resolved is null
                ? PdfNull.Instance
                : CloneContent(target, source, resolved, map, skipParentOn, skipParent);
            target.ReplaceObject(placeholder.ObjectNumber, content);
            return placeholder;
        }
        return CloneContent(target, source, obj, map, skipParentOn, skipParent: false);
    }

    private static PdfObject CloneContent(PdfDocument target, PdfDocument source, PdfObject obj,
        Dictionary<int, PdfIndirectReference> map, HashSet<int>? skipParentOn, bool skipParent)
    {
        switch (obj)
        {
            case PdfDictionary dict:
            {
                var clone = new PdfDictionary();
                foreach (KeyValuePair<PdfName, PdfObject> kv in dict)
                {
                    if (skipParent && kv.Key.Value == "Parent") continue;   // deferred field-tree edge
                    clone[kv.Key] = CloneValue(target, source, kv.Value, map, skipParentOn);
                }
                return clone;
            }
            case PdfArray array:
            {
                var clone = new PdfArray();
                foreach (PdfObject item in array)
                    clone.Add(CloneValue(target, source, item, map, skipParentOn));
                return clone;
            }
            case PdfStream stream:
            {
                var dictClone = new PdfDictionary();
                foreach (KeyValuePair<PdfName, PdfObject> kv in stream.Dictionary)
                    dictClone[kv.Key] = CloneValue(target, source, kv.Value, map, skipParentOn);
                byte[] raw = source.Decryptor is not null && stream.IsIndirect
                    ? source.Decryptor.Decrypt(stream.Data, stream.ObjectNumber, stream.GenerationNumber)
                    : stream.Data;
                return new PdfStream(dictClone, raw);
            }
            // Primitives carry mutable object-identity fields (ObjectNumber/IsIndirect). A primitive reached
            // as the content of an INDIRECT object would be handed to ReplaceObject/AddObject, which would
            // stomp those fields on the source-owned instance. Return a fresh copy so the target never
            // mutates a source object (or a shared PdfName singleton).
            case PdfName name:
                return new PdfName(name.Value);
            case PdfInteger integer:
                return new PdfInteger(integer.LongValue);
            case PdfReal real:
                return new PdfReal(real.Value);
            case PdfString str:
                return new PdfString(str.Bytes);
            default:
                return obj; // PdfBoolean/PdfNull are immutable singletons; their identity fields don't affect output
        }
    }

    /// <summary>Indirect widget annotations on the page that carry a /Parent field-tree edge,
    /// paired with that parent reference. These edges are skipped during the graph clone and
    /// re-established by <see cref="RebuildFieldSpines"/>.</summary>
    private static List<(int WidgetObjNum, PdfIndirectReference SrcParent)> CollectWidgetParents(
        PdfDocument source, PdfDictionary pageCopy)
    {
        var result = new List<(int, PdfIndirectReference)>();
        PdfObject? annotsObj = pageCopy.Get(new PdfName("Annots"));
        if (annotsObj is PdfIndirectReference ar) annotsObj = source.GetObject(ar.ObjectNumber);
        if (annotsObj is not PdfArray annots) return result;
        foreach (PdfObject e in annots)
        {
            if (e is not PdfIndirectReference r) continue;
            if (source.GetObject(r.ObjectNumber) is not PdfDictionary d) continue;
            if (d.Get(PdfName.Subtype) is not PdfName { Value: "Widget" }) continue;
            if (d.TryGetValue(new PdfName("Parent"), out PdfObject parent) && parent is PdfIndirectReference pr)
                result.Add((r.ObjectNumber, pr));
        }
        return result;
    }

    /// <summary>Re-creates each deferred widget's ancestor chain in the target: every ancestor is
    /// cloned once (all entries except /Kids and /Parent), its /Kids rebuilt to hold ONLY the
    /// children actually imported, and /Parent wired upward — so field names, flags, and values
    /// inherit exactly as in the source without importing sibling subtrees.</summary>
    private static void RebuildFieldSpines(PdfDocument target, PdfDocument source,
        Dictionary<int, PdfIndirectReference> map,
        List<(int WidgetObjNum, PdfIndirectReference SrcParent)> deferredParents)
    {
        if (deferredParents.Count == 0) return;
        var kidsName = new PdfName("Kids");
        var parentName = new PdfName("Parent");
        var ancestors = new Dictionary<int, PdfIndirectReference>();   // source obj# → spine clone

        foreach ((int widgetObjNum, PdfIndirectReference srcParent) in deferredParents)
        {
            if (!map.TryGetValue(widgetObjNum, out PdfIndirectReference? childRef)) continue;
            PdfIndirectReference? srcRef = srcParent;
            var guard = 0;
            while (srcRef is not null && guard++ < 64)
            {
                bool existed = ancestors.TryGetValue(srcRef.ObjectNumber, out PdfIndirectReference? ancRef);
                var srcDict = source.GetObject(srcRef.ObjectNumber) as PdfDictionary;
                if (!existed)
                {
                    if (srcDict is null) break;
                    var copy = new PdfDictionary();
                    foreach (KeyValuePair<PdfName, PdfObject> kv in srcDict)
                        if (kv.Key.Value is not ("Kids" or "Parent"))
                            copy[new PdfName(kv.Key.Value)] = CloneValue(target, source, kv.Value, map, null);
                    copy[kidsName] = new PdfArray();
                    ancRef = target.RegisterObject(copy);
                    ancestors[srcRef.ObjectNumber] = ancRef;
                }

                var ancDict = (PdfDictionary)target.GetObject(ancRef!.ObjectNumber)!;
                var kids = (PdfArray)ancDict.Get(kidsName)!;
                if (kids.OfType<PdfIndirectReference>().All(k => k.ObjectNumber != childRef.ObjectNumber))
                    kids.Add(childRef);
                if (target.GetObject(childRef.ObjectNumber) is PdfDictionary childDict)
                    childDict[parentName] = ancRef;

                if (existed) break;   // the spine above this node is already built and wired
                childRef = ancRef;
                srcRef = srcDict!.TryGetValue(parentName, out PdfObject up) ? up as PdfIndirectReference : null;
            }
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
