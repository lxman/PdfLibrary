using System.Globalization;
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
        AnnotationAppearanceGenerator.Generate(doc, annot);
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
        AnnotationAppearanceGenerator.Generate(doc, annot);
    }

    /// <summary>Adds a FreeText annotation and generates its /AP. Returns the annotation's object number.</summary>
    internal static int AddFreeText(PdfDocument doc, PdfDictionary page, PdfIndirectReference pageRef,
        PdfRect rect, string text, double fontSize, PdfColor color, int quadding)
    {
        PdfDictionary annot = NewAnnot(doc, page, pageRef, "FreeText", rect, out PdfIndirectReference annotRef);
        annot[new PdfName("Contents")] = PdfString.FromText(text);
        string da = string.Format(CultureInfo.InvariantCulture,
            "/Helv {0:0.####} Tf {1:0.###} {2:0.###} {3:0.###} rg", fontSize, color.R, color.G, color.B);
        annot[new PdfName("DA")] = PdfString.FromText(da);
        annot[new PdfName("Q")] = new PdfInteger(quadding);

        AnnotationAppearanceGenerator.Generate(doc, annot);
        return annotRef.ObjectNumber;
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

    /// <summary>Adds a Circle markup annotation and generates its /AP. Returns the annotation's object number.</summary>
    internal static int AddCircle(PdfDocument doc, PdfDictionary page, PdfIndirectReference pageRef,
        PdfRect rect, PdfColor stroke, PdfColor? fill, double width)
    {
        PdfDictionary annot = NewAnnot(doc, page, pageRef, "Circle", rect, out PdfIndirectReference annotRef);
        annot[new PdfName("C")] = ColorArray(stroke);
        if (fill.HasValue)
            annot[new PdfName("IC")] = ColorArray(fill.Value);
        annot[new PdfName("BS")] = new PdfDictionary { [new PdfName("W")] = new PdfReal(width) };

        AnnotationAppearanceGenerator.Generate(doc, annot);
        return annotRef.ObjectNumber;
    }

    /// <summary>Adds a Line markup annotation and generates its /AP. /Rect is the endpoints' bounding box, padded by the border width.</summary>
    internal static int AddLine(PdfDocument doc, PdfDictionary page, PdfIndirectReference pageRef,
        double x1, double y1, double x2, double y2, PdfColor color, double width)
    {
        double pad = Math.Max(1.0, width);
        var rect = new PdfRect(
            Math.Min(x1, x2) - pad, Math.Min(y1, y2) - pad,
            Math.Max(x1, x2) + pad, Math.Max(y1, y2) + pad);

        PdfDictionary annot = NewAnnot(doc, page, pageRef, "Line", rect, out PdfIndirectReference annotRef);
        annot[new PdfName("L")] = new PdfArray(new PdfReal(x1), new PdfReal(y1), new PdfReal(x2), new PdfReal(y2));
        annot[new PdfName("C")] = ColorArray(color);
        annot[new PdfName("BS")] = new PdfDictionary { [new PdfName("W")] = new PdfReal(width) };

        AnnotationAppearanceGenerator.Generate(doc, annot);
        return annotRef.ObjectNumber;
    }

    /// <summary>Adds an Ink markup annotation and generates its /AP. /Rect is the bounding box of all points, padded by the border width.</summary>
    internal static int AddInk(PdfDocument doc, PdfDictionary page, PdfIndirectReference pageRef,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> paths, PdfColor color, double width)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (IReadOnlyList<(double X, double Y)> path in paths)
            foreach ((double px, double py) in path)
            {
                minX = Math.Min(minX, px); minY = Math.Min(minY, py);
                maxX = Math.Max(maxX, px); maxY = Math.Max(maxY, py);
            }
        if (minX > maxX) { minX = minY = 0; maxX = maxY = 0; } // no points

        double pad = Math.Max(1.0, width);
        var rect = new PdfRect(minX - pad, minY - pad, maxX + pad, maxY + pad);

        PdfDictionary annot = NewAnnot(doc, page, pageRef, "Ink", rect, out PdfIndirectReference annotRef);

        var inkList = new PdfArray();
        foreach (IReadOnlyList<(double X, double Y)> path in paths)
        {
            var flat = new PdfArray();
            foreach ((double px, double py) in path)
            {
                flat.Add(new PdfReal(px));
                flat.Add(new PdfReal(py));
            }
            inkList.Add(flat);
        }
        annot[new PdfName("InkList")] = inkList;
        annot[new PdfName("C")] = ColorArray(color);
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
