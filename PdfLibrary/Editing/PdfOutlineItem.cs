using System.Text;
using PdfLibrary.Builder.Bookmark;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing;

/// <summary>
/// A single mutable outline (bookmark) item in <see cref="PdfOutlineCollection"/>.
/// Wraps an indirect outline dictionary; mutations edit the dictionary and re-link the tree.
/// </summary>
public sealed class PdfOutlineItem
{
    private readonly PdfDocument _document;
    private readonly OutlineModel _model;
    internal OutlineNode Node { get; }

    internal PdfOutlineItem(PdfDocument document, OutlineModel model, OutlineNode node)
    {
        _document = document;
        _model = model;
        Node = node;
    }

    /// <summary>The item title. ASCII is written as a literal string; non-ASCII as UTF-16BE.</summary>
    public string Title
    {
        get => DecodeTitle(Node.Dict.Get(new PdfName("Title")));
        set => Node.Dict[new PdfName("Title")] = EncodeTitle(value);
    }

    /// <summary>The destination this item navigates to, or null if none/unresolvable.</summary>
    public PdfDestination? Destination
    {
        get
        {
            PdfObject? dest = Node.Dict.Get(new PdfName("Dest"));
            return dest is null ? null : DestinationCodec.Decode(_document, dest);
        }
        set
        {
            if (value is null)
            {
                Node.Dict.Remove(new PdfName("Dest"));
                return;
            }
            Node.Dict[new PdfName("Dest")] = EncodeDestination(_document, value);
        }
    }

    /// <summary>Whether the item is expanded (default true). Controls the sign of /Count.</summary>
    public bool IsOpen
    {
        get => Node.IsOpen;
        set
        {
            Node.IsOpen = value;
            _model.Rewire();
        }
    }

    /// <summary>This item's child outline items.</summary>
    public IReadOnlyList<PdfOutlineItem> Children =>
        Node.Children.Select(c => new PdfOutlineItem(_document, _model, c)).ToList();

    /// <summary>Adds a child pointing to <paramref name="page"/> (whole-page, top, keep zoom).</summary>
    public PdfOutlineItem Add(string title, int page) =>
        Add(title, PdfDestination.ToPage(page));

    /// <summary>Adds a child with an explicit destination and optional nested children.</summary>
    public PdfOutlineItem Add(string title, PdfDestination destination, Action<PdfOutlineItem>? children = null)
    {
        OutlineNode child = _model.CreateNode(title, destination);
        Node.Children.Add(child);
        var item = new PdfOutlineItem(_document, _model, child);
        children?.Invoke(item);
        _model.Rewire();
        return item;
    }

    /// <summary>Removes this item and its entire subtree.</summary>
    public void Remove()
    {
        if (!_model.RemoveNode(Node))
            throw new InvalidOperationException("Outline item is not present in the tree.");
        _model.Rewire();
    }

    /// <summary>Re-parents this item to <paramref name="newParent"/> (null = top level) at <paramref name="index"/>.</summary>
    public void MoveTo(PdfOutlineItem? newParent, int index)
    {
        OutlineNode? targetParent = newParent?.Node;

        // Guard against moving into self or a descendant.
        if (targetParent is not null && (targetParent == Node || IsDescendant(Node, targetParent)))
            throw new InvalidOperationException("Cannot move an outline item into itself or one of its descendants.");

        if (!_model.RemoveNode(Node))
            throw new InvalidOperationException("Outline item is not present in the tree.");

        List<OutlineNode> siblings = targetParent?.Children ?? _model.TopLevel;
        if (index < 0 || index > siblings.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        siblings.Insert(index, Node);
        _model.Rewire();
    }

    private static bool IsDescendant(OutlineNode ancestor, OutlineNode candidate)
    {
        foreach (OutlineNode child in ancestor.Children)
        {
            if (child == candidate || IsDescendant(child, candidate))
                return true;
        }
        return false;
    }

    // ── Title encoding ─────────────────────────────────────────────────────────

    internal static PdfString EncodeTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        bool isAscii = title.All(c => c <= 0x7E && c >= 0x20 || c is '\t' or '\n' or '\r');
        if (isAscii)
            return new PdfString(title);

        // UTF-16BE with BOM (FE FF), per ISO 32000 text-string convention.
        byte[] utf16 = Encoding.BigEndianUnicode.GetBytes(title);
        var bytes = new byte[utf16.Length + 2];
        bytes[0] = 0xFE;
        bytes[1] = 0xFF;
        Array.Copy(utf16, 0, bytes, 2, utf16.Length);
        return new PdfString(bytes);
    }

    internal static string DecodeTitle(PdfObject? title)
    {
        if (title is not PdfString s) return string.Empty;
        byte[] bytes = s.Bytes;
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        return s.Value;
    }

    internal static PdfArray EncodeDestination(PdfDocument doc, PdfDestination dest)
    {
        PdfArray kids = PageTreeOps.Kids(doc);
        if (dest.PageIndex < 0 || dest.PageIndex >= kids.Count)
            throw new ArgumentOutOfRangeException(nameof(dest), "Destination page index is out of range.");
        if (kids[dest.PageIndex] is not PdfIndirectReference pageRef)
            throw new InvalidOperationException("Page is not an indirect reference; cannot encode destination.");
        return DestinationCodec.Encode(dest, pageRef);
    }
}
