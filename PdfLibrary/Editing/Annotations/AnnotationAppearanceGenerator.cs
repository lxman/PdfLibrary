using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing.Forms;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing.Annotations;

/// <summary>
/// Generates an <c>/AP /N</c> Form-XObject appearance stream for a markup annotation, so it renders
/// in this library's <c>PdfRenderer</c> (which draws annotations only from <c>/AP /N</c>) as well as
/// in external viewers. The content is drawn in the appearance BBox space <c>[0 0 w h]</c>; the
/// renderer maps that BBox onto the annotation <c>/Rect</c>.
///
/// Modeled on <see cref="Forms.FieldAppearanceGenerator"/>. Square is the first supported subtype;
/// Circle / Line / Ink / FreeText (and a retrofit for Highlight / Text) follow the same pattern.
/// </summary>
internal static class AnnotationAppearanceGenerator
{
    /// <summary>Builds and attaches <c>/AP /N</c> for the given annotation dictionary, by subtype.</summary>
    public static void Generate(PdfDocument doc, PdfDictionary annot)
    {
        string subtype = annot.Get(new PdfName("Subtype")) is PdfName n ? n.Value : string.Empty;
        switch (subtype)
        {
            case "Square":
                GenerateSquare(doc, annot);
                break;
            case "Circle":
                GenerateCircle(doc, annot);
                break;
            case "Line":
                GenerateLine(doc, annot);
                break;
            case "Ink":
                GenerateInk(doc, annot);
                break;
            case "FreeText":
                GenerateFreeText(doc, annot);
                break;
            case "Highlight":
                GenerateHighlight(doc, annot);
                break;
            case "Text":
                GenerateNoteIcon(doc, annot);
                break;
        }
    }

    private static void GenerateSquare(PdfDocument doc, PdfDictionary annot)
    {
        if (!TryGetRect(annot, out double x0, out double y0, out double x1, out double y1)) return;
        double w = Math.Abs(x1 - x0), h = Math.Abs(y1 - y0);
        if (w <= 0 || h <= 0) return;
        string content = AnnotationContentBuilder.Square(w, h, GetBorderWidth(annot), GetColor(annot, "C"), GetColor(annot, "IC"));
        AttachNormalAppearance(doc, annot, w, h, content);
    }

    private static void GenerateCircle(PdfDocument doc, PdfDictionary annot)
    {
        if (!TryGetRect(annot, out double x0, out double y0, out double x1, out double y1)) return;
        double w = Math.Abs(x1 - x0), h = Math.Abs(y1 - y0);
        if (w <= 0 || h <= 0) return;
        string content = AnnotationContentBuilder.Circle(w, h, GetBorderWidth(annot), GetColor(annot, "C"), GetColor(annot, "IC"));
        AttachNormalAppearance(doc, annot, w, h, content);
    }

    private static void GenerateLine(PdfDocument doc, PdfDictionary annot)
    {
        if (!TryGetRect(annot, out double rx0, out double ry0, out double rx1, out double ry1)) return;
        double w = Math.Abs(rx1 - rx0), h = Math.Abs(ry1 - ry0);
        if (w <= 0 || h <= 0) return;
        if (annot.Get(new PdfName("L")) is not PdfArray { Count: >= 4 } l) return;

        double ox = Math.Min(rx0, rx1), oy = Math.Min(ry0, ry1);
        double[] stroke = GetColor(annot, "C") ?? [0, 0, 0];
        string content = AnnotationContentBuilder.Line(GetBorderWidth(annot), stroke,
            ToDouble(l[0]) - ox, ToDouble(l[1]) - oy, ToDouble(l[2]) - ox, ToDouble(l[3]) - oy);
        AttachNormalAppearance(doc, annot, w, h, content);
    }

    private static void GenerateInk(PdfDocument doc, PdfDictionary annot)
    {
        if (!TryGetRect(annot, out double rx0, out double ry0, out double rx1, out double ry1)) return;
        double w = Math.Abs(rx1 - rx0), h = Math.Abs(ry1 - ry0);
        if (w <= 0 || h <= 0) return;
        if (annot.Get(new PdfName("InkList")) is not PdfArray inkList) return;

        double ox = Math.Min(rx0, rx1), oy = Math.Min(ry0, ry1);
        var localPaths = new List<IReadOnlyList<(double X, double Y)>>();
        foreach (PdfObject pathObj in inkList)
        {
            if (pathObj is not PdfArray pts || pts.Count < 4) continue;
            var pl = new List<(double X, double Y)>();
            for (int i = 0; i + 1 < pts.Count; i += 2)
                pl.Add((ToDouble(pts[i]) - ox, ToDouble(pts[i + 1]) - oy));
            localPaths.Add(pl);
        }
        string content = AnnotationContentBuilder.Ink(GetBorderWidth(annot), GetColor(annot, "C") ?? [0, 0, 0], localPaths);
        AttachNormalAppearance(doc, annot, w, h, content);
    }

    private static void GenerateFreeText(PdfDocument doc, PdfDictionary annot)
    {
        if (!TryGetRect(annot, out double x0, out double y0, out double x1, out double y1)) return;
        double w = Math.Abs(x1 - x0), h = Math.Abs(y1 - y0);
        if (w <= 0 || h <= 0) return;

        string text = annot.Get(new PdfName("Contents")) is PdfString s ? s.GetText() : string.Empty;
        FieldDa da = FieldDaParser.Parse(annot.Get(new PdfName("DA")) is PdfString daStr ? daStr.Value : null);
        double size = da.FontSize > 0 ? da.FontSize : 12.0;
        (string resName, PdfIndirectReference fontRef) = AppearanceFontResolver.Resolve(doc, da.FontName);

        string content = AnnotationContentBuilder.FreeText(h, resName, size, da.ColorOps, text);

        var resources = new PdfDictionary
        {
            [new PdfName("Font")] = new PdfDictionary { [new PdfName(resName)] = fontRef }
        };
        AttachNormalAppearance(doc, annot, w, h, content, resources);
    }

    private static void GenerateHighlight(PdfDocument doc, PdfDictionary annot)
    {
        if (!TryGetRect(annot, out double x0, out double y0, out double x1, out double y1)) return;
        double w = Math.Abs(x1 - x0), h = Math.Abs(y1 - y0);
        if (w <= 0 || h <= 0) return;

        string content = AnnotationContentBuilder.Highlight(w, h, GetColor(annot, "C") ?? [1, 1, 0]);

        // Multiply blend so the highlight tints underlying content instead of covering it.
        var gs = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("ExtGState"),
            [new PdfName("BM")] = new PdfName("Multiply")
        };
        var resources = new PdfDictionary
        {
            [new PdfName("ExtGState")] = new PdfDictionary { [new PdfName("GShl")] = gs }
        };
        AttachNormalAppearance(doc, annot, w, h, content, resources);
    }

    private static void GenerateNoteIcon(PdfDocument doc, PdfDictionary annot)
    {
        if (!TryGetRect(annot, out double x0, out double y0, out double x1, out double y1)) return;
        double w = Math.Abs(x1 - x0), h = Math.Abs(y1 - y0);
        if (w <= 0 || h <= 0) return;
        AttachNormalAppearance(doc, annot, w, h, AnnotationContentBuilder.NoteIcon(w, h));
    }

    private static void AttachNormalAppearance(PdfDocument doc, PdfDictionary annot, double w, double h,
        string content, PdfDictionary? resources = null)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(content);

        var xobjDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Form"),
            [new PdfName("BBox")] = new PdfArray(new PdfReal(0), new PdfReal(0), new PdfReal(w), new PdfReal(h)),
            [new PdfName("Matrix")] = new PdfArray(
                new PdfInteger(1), new PdfInteger(0), new PdfInteger(0), new PdfInteger(1),
                new PdfInteger(0), new PdfInteger(0))
        };
        if (resources is not null)
            xobjDict[new PdfName("Resources")] = resources;

        var stream = new PdfStream(xobjDict, bytes);
        PdfIndirectReference apRef = doc.RegisterObject(stream);

        annot[new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = apRef };
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private static bool TryGetRect(PdfDictionary annot, out double x0, out double y0, out double x1, out double y1)
    {
        x0 = y0 = x1 = y1 = 0;
        if (annot.Get(new PdfName("Rect")) is not PdfArray { Count: >= 4 } a) return false;
        x0 = ToDouble(a[0]); y0 = ToDouble(a[1]); x1 = ToDouble(a[2]); y1 = ToDouble(a[3]);
        return true;
    }

    private static double GetBorderWidth(PdfDictionary annot)
    {
        if (annot.Get(new PdfName("BS")) is PdfDictionary bs && bs.Get(new PdfName("W")) is { } wRaw)
            return ToDouble(wRaw);
        return 1.0;
    }

    private static double[]? GetColor(PdfDictionary annot, string key)
    {
        if (annot.Get(new PdfName(key)) is not PdfArray { Count: >= 3 } a) return null;
        return [ToDouble(a[0]), ToDouble(a[1]), ToDouble(a[2])];
    }

    private static double ToDouble(PdfObject obj) => obj switch
    {
        PdfReal r => r.Value,
        PdfInteger i => i.Value,
        _ => 0.0
    };
}
