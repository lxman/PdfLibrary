using System.Collections;
using PdfLibrary.Builder.Bookmark;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing;

/// <summary>
/// A mutable, live view of the document's outline (bookmark) tree
/// (<c>/Catalog /Outlines</c>). Materializes lazily; mutations re-link the tree
/// and fix the catalog reference.
/// </summary>
public sealed class PdfOutlineCollection : IReadOnlyList<PdfOutlineItem>
{
    private readonly PdfDocument _document;
    private readonly OutlineModel _model;

    internal PdfOutlineCollection(PdfDocument document)
    {
        _document = document;
        _model = new OutlineModel(document);
    }

    /// <summary>Number of top-level items.</summary>
    public int Count => _model.TopLevel.Count;

    public PdfOutlineItem this[int index]
    {
        get
        {
            if (index < 0 || index >= _model.TopLevel.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return new PdfOutlineItem(_document, _model, _model.TopLevel[index]);
        }
    }

    /// <summary>Adds a top-level item pointing to <paramref name="page"/>.</summary>
    public PdfOutlineItem Add(string title, int page) =>
        Add(title, PdfDestination.ToPage(page));

    /// <summary>Adds a top-level item with an explicit destination and optional nested children.</summary>
    public PdfOutlineItem Add(string title, PdfDestination destination, Action<PdfOutlineItem>? children = null)
    {
        _model.EnsureRoot();
        OutlineNode node = _model.CreateNode(title, destination);
        _model.TopLevel.Add(node);
        var item = new PdfOutlineItem(_document, _model, node);
        children?.Invoke(item);
        _model.Rewire();
        return item;
    }

    /// <summary>Removes the top-level item at <paramref name="index"/> (and its subtree).</summary>
    public void RemoveAt(int index) => this[index].Remove();

    /// <summary>Removes the entire outline tree and the catalog reference.</summary>
    public void Clear()
    {
        _model.TopLevel.Clear();
        _model.RemoveRoot();
    }

    public IEnumerator<PdfOutlineItem> GetEnumerator()
    {
        foreach (OutlineNode node in _model.TopLevel)
            yield return new PdfOutlineItem(_document, _model, node);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Owns the in-memory outline node tree, the <c>/Outlines</c> root dictionary/reference,
/// and the re-link logic shared by the collection and its items.
/// </summary>
internal sealed class OutlineModel
{
    private readonly PdfDocument _document;
    private PdfIndirectReference? _rootRef;
    private PdfDictionary? _root;

    public List<OutlineNode> TopLevel { get; }

    public OutlineModel(PdfDocument document)
    {
        _document = document;

        PdfDictionary? catalog = document.CatalogDictionary;
        PdfObject? outlinesRef = catalog?.Get(new PdfName("Outlines"));
        if (outlinesRef is PdfIndirectReference r && document.GetObject(r.ObjectNumber) is PdfDictionary root)
        {
            _rootRef = r;
            _root = root;
            TopLevel = OutlineTree.Build(document, root.Get(new PdfName("First")));
        }
        else if (Resolve(outlinesRef) is PdfDictionary directRoot)
        {
            // Promote a direct /Outlines dict to indirect so it can be referenced uniformly.
            _root = directRoot;
            _rootRef = document.RegisterObject(directRoot);
            catalog![new PdfName("Outlines")] = _rootRef;
            TopLevel = OutlineTree.Build(document, directRoot.Get(new PdfName("First")));
        }
        else
        {
            TopLevel = [];
        }
    }

    /// <summary>Creates the /Outlines root dict + catalog reference if not yet present.</summary>
    public void EnsureRoot()
    {
        if (_root is not null) return;
        PdfDictionary catalog = _document.CatalogDictionary
            ?? throw new InvalidOperationException("Document has no catalog.");
        _root = new PdfDictionary { [PdfName.TypeName] = new PdfName("Outlines") };
        _rootRef = _document.RegisterObject(_root);
        catalog[new PdfName("Outlines")] = _rootRef;
    }

    /// <summary>Removes the /Outlines root and the catalog reference.</summary>
    public void RemoveRoot()
    {
        _document.CatalogDictionary?.Remove(new PdfName("Outlines"));
        if (_rootRef is not null)
            _document.RemoveObject(_rootRef.ObjectNumber);
        _root = null;
        _rootRef = null;
    }

    /// <summary>Allocates a fresh indirect outline-item dict for the given title + destination.</summary>
    public OutlineNode CreateNode(string title, PdfDestination destination)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Title")] = PdfOutlineItem.EncodeTitle(title),
            [new PdfName("Dest")] = PdfOutlineItem.EncodeDestination(_document, destination)
        };
        PdfIndirectReference reference = _document.RegisterObject(dict);
        return new OutlineNode { Dict = dict, Ref = reference, Children = [], IsOpen = true };
    }

    /// <summary>Removes <paramref name="target"/> (and its subtree) from the in-memory tree.</summary>
    public bool RemoveNode(OutlineNode target) => RemoveFrom(TopLevel, target);

    private static bool RemoveFrom(List<OutlineNode> nodes, OutlineNode target)
    {
        if (nodes.Remove(target)) return true;
        foreach (OutlineNode node in nodes)
            if (RemoveFrom(node.Children, target))
                return true;
        return false;
    }

    /// <summary>Re-links /First /Last /Prev /Next /Parent /Count and the /Outlines root.</summary>
    public void Rewire()
    {
        if (_root is null || _rootRef is null)
        {
            if (TopLevel.Count == 0) return;
            EnsureRoot();
        }

        (PdfObject? first, PdfObject? last, int count) = OutlineTree.Rewire(_rootRef!, TopLevel);
        SetOrRemove(_root!, "First", first);
        SetOrRemove(_root!, "Last", last);
        _root![new PdfName("Count")] = new PdfInteger(count);
    }

    private PdfObject? Resolve(PdfObject? obj) =>
        obj is PdfIndirectReference r ? _document.GetObject(r.ObjectNumber) : obj;

    private static void SetOrRemove(PdfDictionary dict, string key, PdfObject? value)
    {
        if (value is null) dict.Remove(new PdfName(key));
        else dict[new PdfName(key)] = value;
    }
}
