using System.Collections;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing;

/// <summary>The page sub-area of <see cref="PdfDocumentEditor"/>: a live view plus page operations.</summary>
public sealed class PdfPageCollection : IReadOnlyList<PdfPage>
{
    private readonly PdfDocument _document;

    internal PdfPageCollection(PdfDocument document) => _document = document;

    public int Count => PageTreeOps.PageDicts(_document).Count;

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

    public void Rotate(int index, int degrees)
    {
        if (degrees % 90 != 0)
            throw new ArgumentException("Rotation must be a multiple of 90 degrees.", nameof(degrees));
        PageAt(index)[new PdfName("Rotate")] = new PdfInteger(((degrees % 360) + 360) % 360);
    }

    public void RotateBy(int index, int delta)
    {
        if (delta % 90 != 0)
            throw new ArgumentException("Rotation must be a multiple of 90 degrees.", nameof(delta));
        PdfDictionary page = PageAt(index);
        int current = page.TryGetValue(new PdfName("Rotate"), out PdfObject o) && o is PdfInteger r ? r.Value : 0;
        page[new PdfName("Rotate")] = new PdfInteger((((current + delta) % 360) + 360) % 360);
    }

    public void Move(int fromIndex, int toIndex) => PageTreeOps.Move(_document, fromIndex, toIndex);

    public void RemoveAt(int index)
    {
        if (Count <= 1)
            throw new InvalidOperationException("Cannot remove the last remaining page.");
        PageTreeOps.RemoveAt(_document, index);
    }

    public PdfPage InsertBlank(int at, double width, double height)
    {
        var page = new PdfDictionary();
        page[PdfName.TypeName] = new PdfName("Page");
        page[new PdfName("MediaBox")] =
            new PdfArray(new PdfReal(0), new PdfReal(0), new PdfReal(width), new PdfReal(height));
        PdfIndirectReference pageRef = _document.RegisterObject(page);
        PageTreeOps.InsertPageRef(_document, pageRef, at);
        return new PdfPage(page, _document, _document.PageTreeRootDictionary);
    }

    public PdfPage Import(PdfDocument source, int sourceIndex, int at)
    {
        ArgumentNullException.ThrowIfNull(source);
        PdfPage srcPage = source.GetPage(sourceIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(sourceIndex));
        PdfIndirectReference newRef = ObjectGraphCloner.CloneInto(_document, source, srcPage.Dictionary);
        PageTreeOps.InsertPageRef(_document, newRef, at);
        var page = (PdfDictionary)_document.GetObject(newRef.ObjectNumber)!;
        return new PdfPage(page, _document, _document.PageTreeRootDictionary);
    }

    public PdfPage Duplicate(int index, int at)
    {
        PdfDictionary src = PageAt(index);
        PdfIndirectReference newRef = ObjectGraphCloner.CloneInto(_document, _document, src);
        PageTreeOps.InsertPageRef(_document, newRef, at);
        var page = (PdfDictionary)_document.GetObject(newRef.ObjectNumber)!;
        return new PdfPage(page, _document, _document.PageTreeRootDictionary);
    }

    public void Append(PdfDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);
        int count = source.PageCount;
        for (var i = 0; i < count; i++)
            Import(source, i, Count);
    }

    public void AppendRange(PdfDocument source, int start, int count)
    {
        ArgumentNullException.ThrowIfNull(source);
        for (var i = 0; i < count; i++)
            Import(source, start + i, Count);
    }
}
