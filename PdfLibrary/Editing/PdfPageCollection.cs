using System.Collections;
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
}
