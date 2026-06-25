using PdfLibrary.Builder;        // PdfRect
using PdfLibrary.Builder.Page;   // PdfColor
using PdfLibrary.Core;           // PdfObject
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

    /// <summary>Returns a read-only snapshot of the annotations on the page at <paramref name="index"/> (empty if none).</summary>
    public IReadOnlyList<PdfAnnotationInfo> GetAnnotations(int index)
    {
        PdfDictionary page = PageAt(index);
        var result = new List<PdfAnnotationInfo>();
        if (Resolve(page.Get(new PdfName("Annots"))) is not PdfArray annots)
            return result;

        foreach (PdfObject entry in annots)
        {
            if (Resolve(entry) is not PdfDictionary annot)
                continue;
            result.Add(new PdfAnnotationInfo
            {
                Subtype = Resolve(annot.Get(new PdfName("Subtype"))) is PdfName n ? n.Value : string.Empty,
                Rect = ReadRect(annot),
                Contents = Resolve(annot.Get(new PdfName("Contents"))) is PdfString s ? s.GetText() : null
            });
        }
        return result;
    }

    /// <summary>
    /// Removes the annotation at <paramref name="annotationIndex"/> from the page at <paramref name="index"/>.
    /// The annotation order matches <see cref="GetAnnotations(int)"/>. Throws if the index is out of range.
    /// </summary>
    public void RemoveAnnotationAt(int index, int annotationIndex)
    {
        PdfDictionary page = PageAt(index);
        if (Resolve(page.Get(new PdfName("Annots"))) is not PdfArray annots
            || annotationIndex < 0 || annotationIndex >= annots.Count)
            throw new ArgumentOutOfRangeException(nameof(annotationIndex));

        PdfObject entry = annots[annotationIndex];
        annots.RemoveAt(annotationIndex);
        if (entry is PdfIndirectReference r)
            _document.RemoveObject(r.ObjectNumber);
    }

    private PdfRect ReadRect(PdfDictionary annot)
    {
        if (Resolve(annot.Get(new PdfName("Rect"))) is PdfArray a && a.Count >= 4)
        {
            double Num(int i) => Resolve(a[i]) switch
            {
                PdfReal r => r.Value,
                PdfInteger n => n.Value,
                _ => 0
            };
            return new PdfRect(Num(0), Num(1), Num(2), Num(3));
        }
        return new PdfRect(0, 0, 0, 0);
    }

    private PdfObject? Resolve(PdfObject? obj) =>
        obj is PdfIndirectReference r ? _document.GetObject(r.ObjectNumber) : obj;

    private PdfIndirectReference PageRef(int index)
    {
        PdfArray kids = PageTreeOps.Kids(_document);
        if (index < 0 || index >= kids.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return (PdfIndirectReference)kids[index];
    }
}
