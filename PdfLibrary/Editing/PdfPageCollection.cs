using System.Collections;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing;

/// <summary>The page sub-area of <see cref="PdfDocumentEditor"/>: a live view plus page operations.</summary>
public sealed partial class PdfPageCollection : IReadOnlyList<PdfPage>
{
    private readonly PdfDocument _document;

    internal PdfPageCollection(PdfDocument document) => _document = document;

    /// <summary>The number of pages in the document.</summary>
    public int Count => PageTreeOps.PageDicts(_document).Count;

    /// <summary>The page at the given zero-based <paramref name="index"/>.</summary>
    public PdfPage this[int index]
    {
        get
        {
            IReadOnlyList<PdfDictionary> pages = PageTreeOps.PageDicts(_document);
            if (index < 0 || index >= pages.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return new PdfPage(pages[index], _document, _document.PageTreeRootDictionary);
        }
    }

    /// <summary>Enumerates the pages in document order.</summary>
    public IEnumerator<PdfPage> GetEnumerator()
    {
        foreach (PdfDictionary dict in PageTreeOps.PageDicts(_document))
            yield return new PdfPage(dict, _document, _document.PageTreeRootDictionary);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private PdfDictionary PageAt(int index)
    {
        IReadOnlyList<PdfDictionary> pages = PageTreeOps.PageDicts(_document);
        if (index < 0 || index >= pages.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return pages[index];
    }

    /// <summary>Sets the absolute rotation of the page at <paramref name="index"/> (a multiple of 90 degrees).</summary>
    public void Rotate(int index, int degrees)
    {
        if (degrees % 90 != 0)
            throw new ArgumentException("Rotation must be a multiple of 90 degrees.", nameof(degrees));
        PageAt(index)[new PdfName("Rotate")] = new PdfInteger(((degrees % 360) + 360) % 360);
    }

    /// <summary>Adds <paramref name="delta"/> degrees (a multiple of 90) to the current rotation of the page at <paramref name="index"/>.</summary>
    public void RotateBy(int index, int delta)
    {
        if (delta % 90 != 0)
            throw new ArgumentException("Rotation must be a multiple of 90 degrees.", nameof(delta));
        PdfDictionary page = PageAt(index);
        int current = page.TryGetValue(new PdfName("Rotate"), out PdfObject o) && o is PdfInteger r ? r.Value : 0;
        page[new PdfName("Rotate")] = new PdfInteger((((current + delta) % 360) + 360) % 360);
    }

    /// <summary>Moves the page at <paramref name="fromIndex"/> to <paramref name="toIndex"/>.</summary>
    public void Move(int fromIndex, int toIndex)
    {
        PageTreeOps.Move(_document, fromIndex, toIndex);
    }

    /// <summary>Removes the page at <paramref name="index"/>. Throws if it is the last remaining page.</summary>
    public void RemoveAt(int index)
    {
        if (Count <= 1)
            throw new InvalidOperationException("Cannot remove the last remaining page.");
        PdfDictionary removed = PageTreeOps.RemoveAt(_document, index);
        DestinationRepairer.OnPageRemoved(_document, removed);
    }

    /// <summary>Inserts a blank page of the given size (in points) at position <paramref name="at"/> and returns it.</summary>
    public PdfPage InsertBlank(int at, double width, double height)
    {
        var page = new PdfDictionary
        {
            [PdfName.TypeName] = new PdfName("Page"),
            [new PdfName("MediaBox")] = new PdfArray(new PdfReal(0), new PdfReal(0), new PdfReal(width), new PdfReal(height))
        };
        PdfIndirectReference pageRef = _document.RegisterObject(page);
        PageTreeOps.InsertPageRef(_document, pageRef, at);
        return new PdfPage(page, _document, _document.PageTreeRootDictionary);
    }

    /// <summary>Copies page <paramref name="sourceIndex"/> from <paramref name="source"/> into this document at position <paramref name="at"/> and returns it.</summary>
    public PdfPage Import(PdfDocument source, int sourceIndex, int at)
    {
        ArgumentNullException.ThrowIfNull(source);
        PdfPage srcPage = source.GetPage(sourceIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(sourceIndex));
        PdfIndirectReference newRef = ObjectGraphCloner.CloneInto(_document, source, srcPage.Dictionary);
        PageTreeOps.InsertPageRef(_document, newRef, at);
        var page = (PdfDictionary)_document.GetObject(newRef.ObjectNumber)!;
        AcroFormMerger.MergeImportedFields(_document, source, page);
        return new PdfPage(page, _document, _document.PageTreeRootDictionary);
    }

    /// <summary>Duplicates the page at <paramref name="index"/>, inserting the copy at position <paramref name="at"/>, and returns it.</summary>
    public PdfPage Duplicate(int index, int at)
    {
        PdfDictionary src = PageAt(index);
        PdfIndirectReference newRef = ObjectGraphCloner.CloneInto(_document, _document, src);
        PageTreeOps.InsertPageRef(_document, newRef, at);
        var page = (PdfDictionary)_document.GetObject(newRef.ObjectNumber)!;
        AcroFormMerger.MergeImportedFields(_document, _document, page);
        return new PdfPage(page, _document, _document.PageTreeRootDictionary);
    }

    /// <summary>Appends all pages from <paramref name="source"/> to the end of this document.</summary>
    public void Append(PdfDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);
        int count = source.PageCount;
        for (var i = 0; i < count; i++)
            Import(source, i, Count);
    }

    /// <summary>Appends <paramref name="count"/> pages from <paramref name="source"/> starting at <paramref name="start"/>.</summary>
    public void AppendRange(PdfDocument source, int start, int count)
    {
        ArgumentNullException.ThrowIfNull(source);
        for (var i = 0; i < count; i++)
            Import(source, start + i, Count);
    }
}
