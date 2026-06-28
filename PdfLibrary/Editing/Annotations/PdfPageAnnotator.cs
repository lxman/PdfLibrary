using PdfLibrary.Builder;        // PdfRect
using PdfLibrary.Builder.Page;   // PdfColor
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing.Annotations;

/// <summary>Builds annotation dictionaries directly and appends them to a page's /Annots.</summary>
internal static class PdfPageAnnotator
{
    internal static void AddNote(PdfDocument doc, PdfDictionary page, PdfIndirectReference pageRef,
        double x, double y, string contents)
    {
        PdfDictionary annot = NewAnnot(doc, page, pageRef, "Text", new PdfRect(x, y - 24, x + 24, y), out _);
        annot[new PdfName("Contents")] = PdfString.FromText(contents);
        annot[new PdfName("Name")] = new PdfName("Note");
    }

    internal static void AddLink(PdfDocument doc, PdfDictionary page, PdfIndirectReference pageRef,
        PdfRect rect, PdfIndirectReference targetPageRef)
    {
        PdfDictionary annot = NewAnnot(doc, page, pageRef, "Link", rect, out _);
        annot[new PdfName("Dest")] = new PdfArray(targetPageRef, new PdfName("Fit"));
        annot[new PdfName("H")] = new PdfName("I");
    }

    internal static void AddExternalLink(PdfDocument doc, PdfDictionary page, PdfIndirectReference pageRef,
        PdfRect rect, string url)
    {
        PdfDictionary annot = NewAnnot(doc, page, pageRef, "Link", rect, out _);
        var action = new PdfDictionary();
        action[PdfName.TypeName] = new PdfName("Action");
        action[new PdfName("S")] = new PdfName("URI");
        action[new PdfName("URI")] = PdfString.FromByteLiteral(url);
        annot[new PdfName("A")] = action;
        annot[new PdfName("H")] = new PdfName("I");
    }

    internal static void AddHighlight(PdfDocument doc, PdfDictionary page, PdfIndirectReference pageRef,
        PdfRect rect, PdfColor color)
    {
        PdfDictionary annot = NewAnnot(doc, page, pageRef, "Highlight", rect, out _);
        annot[new PdfName("C")] = ColorArray(color);
        annot[new PdfName("QuadPoints")] = new PdfArray(
            new PdfReal(rect.Left), new PdfReal(rect.Top),
            new PdfReal(rect.Right), new PdfReal(rect.Top),
            new PdfReal(rect.Left), new PdfReal(rect.Bottom),
            new PdfReal(rect.Right), new PdfReal(rect.Bottom));
    }

    /// <summary>Adds a Square markup annotation and generates its /AP. Returns the annotation's object number.</summary>
    internal static int AddSquare(PdfDocument doc, PdfDictionary page, PdfIndirectReference pageRef,
        PdfRect rect, PdfColor stroke, PdfColor? fill, double width)
    {
        PdfDictionary annot = NewAnnot(doc, page, pageRef, "Square", rect, out PdfIndirectReference annotRef);
        annot[new PdfName("C")] = ColorArray(stroke);
        if (fill.HasValue)
            annot[new PdfName("IC")] = ColorArray(fill.Value);
        annot[new PdfName("BS")] = new PdfDictionary { [new PdfName("W")] = new PdfReal(width) };

        AnnotationAppearanceGenerator.Generate(doc, annot);
        return annotRef.ObjectNumber;
    }

    private static PdfArray ColorArray(PdfColor c) =>
        new(new PdfReal(c.R), new PdfReal(c.G), new PdfReal(c.B));

    private static PdfDictionary NewAnnot(PdfDocument doc, PdfDictionary page, PdfIndirectReference pageRef,
        string subtype, PdfRect rect, out PdfIndirectReference annotRef)
    {
        var annot = new PdfDictionary();
        annot[PdfName.TypeName] = new PdfName("Annot");
        annot[PdfName.Subtype] = new PdfName(subtype);
        annot[new PdfName("Rect")] = new PdfArray(
            new PdfReal(rect.Left), new PdfReal(rect.Bottom), new PdfReal(rect.Right), new PdfReal(rect.Top));
        annot[new PdfName("P")] = pageRef;
        annotRef = doc.RegisterObject(annot);
        AppendToAnnots(doc, page, annotRef);
        return annot;
    }

    private static void AppendToAnnots(PdfDocument doc, PdfDictionary page, PdfIndirectReference annotRef)
    {
        PdfObject? a = page.Get(new PdfName("Annots"));
        if (a is PdfIndirectReference r) a = doc.GetObject(r.ObjectNumber);
        if (a is PdfArray arr) { arr.Add(annotRef); return; }
        var created = new PdfArray();
        created.Add(annotRef);
        page[new PdfName("Annots")] = created;
    }
}
