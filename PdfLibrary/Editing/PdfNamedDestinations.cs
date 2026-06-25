using System.Collections;
using PdfLibrary.Builder.Bookmark;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing;

/// <summary>
/// A live view of the document's named destinations.
/// Reads from both the legacy <c>/Catalog /Dests</c> dictionary and the modern
/// <c>/Catalog /Names /Dests</c> name tree. Writes new names to the modern tree,
/// and updates existing legacy-<c>/Dests</c> names in place.
/// The name tree is kept as a single flat node with <c>/Names</c> pairs sorted by name
/// (ISO 32000 §7.9.6).
/// </summary>
public sealed class PdfNamedDestinations : IReadOnlyCollection<string>
{
    private readonly PdfDocument _document;

    internal PdfNamedDestinations(PdfDocument document) => _document = document;

    // ── IReadOnlyCollection<string> ────────────────────────────────────────

    public int Count => AllNames().Count;

    public IEnumerator<string> GetEnumerator() => AllNames().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Sets a named destination. If the name exists in the legacy <c>/Dests</c> dict it is
    /// updated there; otherwise the modern name tree is written (sorted by name).
    /// </summary>
    public void Set(string name, PdfDestination destination)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(destination);

        PdfArray destArr = EncodeDestination(destination);

        // Check legacy /Dests first; update in place if found there.
        if (GetLegacyDestsDict() is PdfDictionary legacy
            && legacy.ContainsKey(new PdfName(name)))
        {
            legacy[new PdfName(name)] = destArr;
            return;
        }

        // Write/update in the modern name tree.
        SetInNameTree(name, destArr);
    }

    /// <summary>Returns the decoded destination for <paramref name="name"/>, or null if not found.</summary>
    public PdfDestination? Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        PdfObject? raw = DestinationRepairer.LookupNamedDest(_document, name);
        return raw is null ? null : DestinationCodec.Decode(_document, raw);
    }

    /// <summary>Returns the decoded destination for <paramref name="name"/>, or null if not found.</summary>
    public PdfDestination? this[string name] => Get(name);

    /// <summary>Enumerates each named destination as a (name, destination) pair.</summary>
    public IEnumerable<KeyValuePair<string, PdfDestination>> Entries()
    {
        foreach (string name in AllNames())
        {
            PdfDestination? dest = Get(name);
            if (dest is not null)
                yield return new KeyValuePair<string, PdfDestination>(name, dest);
        }
    }

    /// <summary>
    /// Renames a destination: Get + Remove(old) + Set(new).
    /// Returns false if <paramref name="oldName"/> does not exist.
    /// </summary>
    public bool Rename(string oldName, string newName)
    {
        ArgumentNullException.ThrowIfNull(oldName);
        ArgumentNullException.ThrowIfNull(newName);

        PdfDestination? dest = Get(oldName);
        if (dest is null) return false;

        Remove(oldName);
        Set(newName, dest);
        return true;
    }

    /// <summary>
    /// Removes a named destination from whichever structure holds it.
    /// Returns false if not found.
    /// </summary>
    public bool Remove(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        bool removed = false;

        // Remove from legacy /Dests if present
        if (GetLegacyDestsDict() is PdfDictionary legacy)
        {
            var key = new PdfName(name);
            if (legacy.ContainsKey(key))
            {
                legacy.Remove(key);
                removed = true;
            }
        }

        // Remove from name tree if present
        if (GetNameTreeDestsDict() is PdfDictionary tree)
        {
            if (RemoveFromNameArray(tree, name))
                removed = true;
        }

        return removed;
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private List<string> AllNames()
    {
        var result = new HashSet<string>();

        // Legacy /Dests
        if (GetLegacyDestsDict() is PdfDictionary legacy)
            foreach (PdfName key in legacy.Keys)
                result.Add(key.Value);

        // Modern name tree
        if (GetNameTreeDestsDict() is PdfDictionary tree)
            CollectNamesFromTree(tree, result);

        return result.ToList();
    }

    private void CollectNamesFromTree(PdfDictionary node, HashSet<string> names)
    {
        if (Resolve(node.Get(new PdfName("Names"))) is PdfArray arr)
            for (var i = 0; i + 1 < arr.Count; i += 2)
                if (arr[i] is PdfString key)
                    names.Add(key.Value);

        if (Resolve(node.Get(new PdfName("Kids"))) is PdfArray kids)
            foreach (PdfObject kid in kids)
                if (Resolve(kid) is PdfDictionary child)
                    CollectNamesFromTree(child, names);
    }

    private PdfDictionary? GetLegacyDestsDict()
    {
        PdfDictionary? catalog = _document.CatalogDictionary;
        if (catalog is null) return null;
        return Resolve(catalog.Get(new PdfName("Dests"))) as PdfDictionary;
    }

    private PdfDictionary? GetNameTreeDestsDict()
    {
        PdfDictionary? catalog = _document.CatalogDictionary;
        if (catalog is null) return null;
        if (Resolve(catalog.Get(new PdfName("Names"))) is not PdfDictionary namesDict) return null;
        return Resolve(namesDict.Get(new PdfName("Dests"))) as PdfDictionary;
    }

    /// <summary>
    /// Writes (or overwrites) a name in the modern name tree flat node,
    /// sorted by name per ISO 32000 §7.9.6.
    /// </summary>
    private void SetInNameTree(string name, PdfArray destArr)
    {
        PdfDictionary catalog = _document.CatalogDictionary
            ?? throw new InvalidOperationException("Document has no catalog.");

        // Ensure /Names dict exists
        PdfDictionary namesDict;
        PdfObject? namesDictObj = catalog.Get(new PdfName("Names"));
        if (Resolve(namesDictObj) is PdfDictionary existing)
        {
            namesDict = existing;
        }
        else
        {
            namesDict = new PdfDictionary();
            catalog[new PdfName("Names")] = _document.RegisterObject(namesDict);
        }

        // Ensure /Names /Dests node exists
        PdfDictionary destsNode;
        PdfObject? destsNodeObj = namesDict.Get(new PdfName("Dests"));
        if (Resolve(destsNodeObj) is PdfDictionary existingDests)
        {
            destsNode = existingDests;
        }
        else
        {
            destsNode = new PdfDictionary();
            namesDict[new PdfName("Dests")] = _document.RegisterObject(destsNode);
        }

        // Read current /Names array (flat pairs: [PdfString name, destArray, ...])
        var pairs = new SortedDictionary<string, PdfArray>(StringComparer.Ordinal);

        // Gather existing
        if (Resolve(destsNode.Get(new PdfName("Names"))) is PdfArray existingArr)
        {
            for (var i = 0; i + 1 < existingArr.Count; i += 2)
            {
                if (existingArr[i] is PdfString k)
                    pairs[k.Value] = (PdfArray)Resolve(existingArr[i + 1])!;
            }
        }

        // Set (overwrite or insert)
        pairs[name] = destArr;

        // Write back sorted
        var newArr = new PdfArray();
        foreach (KeyValuePair<string, PdfArray> kv in pairs)
        {
            newArr.Add(PdfString.FromByteLiteral(kv.Key));
            newArr.Add(kv.Value);
        }
        destsNode[new PdfName("Names")] = newArr;
    }

    /// <summary>Removes a name from the flat /Names array in the given tree node. Returns true if found.</summary>
    private bool RemoveFromNameArray(PdfDictionary node, string name)
    {
        if (Resolve(node.Get(new PdfName("Names"))) is PdfArray arr)
        {
            for (var i = 0; i + 1 < arr.Count; i += 2)
            {
                if (arr[i] is PdfString key && key.Value == name)
                {
                    arr.RemoveAt(i + 1);
                    arr.RemoveAt(i);
                    return true;
                }
            }
        }

        // Recurse into kids for multi-level trees
        if (Resolve(node.Get(new PdfName("Kids"))) is PdfArray kids)
        {
            foreach (PdfObject kid in kids)
            {
                if (Resolve(kid) is PdfDictionary child && RemoveFromNameArray(child, name))
                    return true;
            }
        }

        return false;
    }

    private PdfArray EncodeDestination(PdfDestination dest)
    {
        // Resolve the page indirect ref from the current page order
        PdfArray kids = PageTreeOps.Kids(_document);
        if (dest.PageIndex < 0 || dest.PageIndex >= kids.Count)
            throw new ArgumentOutOfRangeException(nameof(dest), $"Page index {dest.PageIndex} is out of range.");
        var pageRef = (PdfIndirectReference)kids[dest.PageIndex];
        return DestinationCodec.Encode(dest, pageRef);
    }

    private PdfObject? Resolve(PdfObject? obj) =>
        obj is PdfIndirectReference r ? _document.GetObject(r.ObjectNumber) : obj;
}
