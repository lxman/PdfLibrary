using PdfLibrary.Builder;        // PdfRect
using PdfLibrary.Builder.Page;   // PdfColor
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing.Annotations;

namespace PdfLibrary.Editing;

public sealed partial class PdfPageCollection
{
    public void AddNote(int index, double x, double y, string contents)
    {
        PdfDictionary page = PageAt(index);
        PdfPageAnnotator.AddNote(_document, page, PageRef(index), x, y, contents);
    }

    public void AddLink(int index, PdfRect rect, int targetPageIndex)
    {
        PdfDictionary page = PageAt(index);
        PdfPageAnnotator.AddLink(_document, page, PageRef(index), rect, PageRef(targetPageIndex));
    }

    public void AddExternalLink(int index, PdfRect rect, string url)
    {
        PdfDictionary page = PageAt(index);
        PdfPageAnnotator.AddExternalLink(_document, page, PageRef(index), rect, url);
    }

    public void AddHighlight(int index, PdfRect rect, PdfColor? color = null)
    {
        PdfDictionary page = PageAt(index);
        PdfPageAnnotator.AddHighlight(_document, page, PageRef(index), rect, color ?? PdfColor.Yellow);
    }

    private PdfIndirectReference PageRef(int index)
    {
        PdfArray kids = PageTreeOps.Kids(_document);
        if (index < 0 || index >= kids.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return (PdfIndirectReference)kids[index];
    }
}
