using PdfLibrary.Builder;        // PdfRect
using PdfLibrary.Builder.Page;   // PdfColor
using PdfLibrary.Core;           // PdfObject
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing.Annotations;

namespace PdfLibrary.Editing;

public sealed partial class PdfPageCollection
{
    /// <summary>Adds a text (sticky-note) annotation at (<paramref name="x"/>, <paramref name="y"/>) on the page at <paramref name="index"/>.</summary>
    public void AddNote(int index, double x, double y, string contents)
    {
        PdfDictionary page = PageAt(index);
        PdfPageAnnotator.AddNote(_document, page, PageRef(index), x, y, contents);
    }

    /// <summary>Adds an internal link over <paramref name="rect"/> that navigates to <paramref name="targetPageIndex"/>.</summary>
    public void AddLink(int index, PdfRect rect, int targetPageIndex)
    {
        PdfDictionary page = PageAt(index);
        PdfPageAnnotator.AddLink(_document, page, PageRef(index), rect, PageRef(targetPageIndex));
    }

    /// <summary>Adds a link over <paramref name="rect"/> that opens an external <paramref name="url"/>.</summary>
    public void AddExternalLink(int index, PdfRect rect, string url)
    {
        PdfDictionary page = PageAt(index);
        PdfPageAnnotator.AddExternalLink(_document, page, PageRef(index), rect, url);
    }

    /// <summary>Adds a highlight over <paramref name="rect"/> (default colour yellow).</summary>
    public void AddHighlight(int index, PdfRect rect, PdfColor? color = null)
    {
        PdfDictionary page = PageAt(index);
        PdfPageAnnotator.AddHighlight(_document, page, PageRef(index), rect, color ?? PdfColor.Yellow);
    }

    /// <summary>
    /// Adds a Square (rectangle) markup annotation over <paramref name="rect"/> with the given
    /// <paramref name="stroke"/> border colour, optional <paramref name="fill"/> interior, and border
    /// <paramref name="width"/>. A real <c>/AP</c> appearance stream is generated so it renders in this
    /// library's renderer and in external viewers. Returns the annotation's stable id for later
    /// <see cref="RemoveAnnotation(int, int)"/>.
    /// </summary>
    public int AddSquare(int index, PdfRect rect, PdfColor stroke, PdfColor? fill = null, double width = 1.0)
    {
        PdfDictionary page = PageAt(index);
        return PdfPageAnnotator.AddSquare(_document, page, PageRef(index), rect, stroke, fill, width);
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
                Contents = Resolve(annot.Get(new PdfName("Contents"))) is PdfString s ? s.GetText() : null,
                AnnotationId = entry is PdfIndirectReference er ? er.ObjectNumber : 0,
                StrokeColor = ReadColor(annot, "C"),
                InteriorColor = ReadColor(annot, "IC"),
                BorderWidth = ReadBorderWidth(annot)
            });
        }
        return result;
    }

    /// <summary>
    /// Removes the annotation with the given <paramref name="annotationId"/> (its PDF object number,
    /// from <see cref="PdfAnnotationInfo.AnnotationId"/>) from the page at <paramref name="index"/>.
    /// No-op if no annotation on that page has the id.
    /// </summary>
    public void RemoveAnnotation(int index, int annotationId)
    {
        PdfDictionary page = PageAt(index);
        if (Resolve(page.Get(new PdfName("Annots"))) is not PdfArray annots)
            return;

        for (int i = 0; i < annots.Count; i++)
        {
            if (annots[i] is not PdfIndirectReference r || r.ObjectNumber != annotationId)
                continue;
            annots.RemoveAt(i);
            _document.RemoveObject(r.ObjectNumber);
            return;
        }
    }

    private PdfColor? ReadColor(PdfDictionary annot, string key)
    {
        if (Resolve(annot.Get(new PdfName(key))) is not PdfArray { Count: >= 3 } a)
            return null;
        double Num(int i) => Resolve(a[i]) switch
        {
            PdfReal r => r.Value,
            PdfInteger n => n.Value,
            _ => 0
        };
        return PdfColor.FromRgb(Num(0), Num(1), Num(2));
    }

    private double? ReadBorderWidth(PdfDictionary annot)
    {
        if (Resolve(annot.Get(new PdfName("BS"))) is not PdfDictionary bs)
            return null;
        return Resolve(bs.Get(new PdfName("W"))) switch
        {
            PdfReal r => r.Value,
            PdfInteger n => n.Value,
            _ => null
        };
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
        if (Resolve(annot.Get(new PdfName("Rect"))) is PdfArray { Count: >= 4 } a)
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
