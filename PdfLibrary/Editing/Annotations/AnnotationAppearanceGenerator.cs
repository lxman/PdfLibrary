using System.Globalization;
using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
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
            // Circle / Line / Ink / FreeText / Highlight / Text — added as the feature fans out.
        }
    }

    private static void GenerateSquare(PdfDocument doc, PdfDictionary annot)
    {
        if (!TryGetRect(annot, out double x0, out double y0, out double x1, out double y1)) return;
        double w = Math.Abs(x1 - x0), h = Math.Abs(y1 - y0);
        if (w <= 0 || h <= 0) return;

        double lw = GetBorderWidth(annot);
        double[]? stroke = GetColor(annot, "C");
        double[]? fill = GetColor(annot, "IC");

        // Inset the rectangle by half the line width so the stroke stays inside the BBox.
        double inset = lw / 2.0;
        double rw = Math.Max(0, w - lw);
        double rh = Math.Max(0, h - lw);

        var sb = new StringBuilder();
        sb.Append("q\n");
        sb.Append(Num(lw)).Append(" w\n");
        if (fill is not null)
            sb.Append(Num(fill[0])).Append(' ').Append(Num(fill[1])).Append(' ').Append(Num(fill[2])).Append(" rg\n");
        if (stroke is not null)
            sb.Append(Num(stroke[0])).Append(' ').Append(Num(stroke[1])).Append(' ').Append(Num(stroke[2])).Append(" RG\n");
        sb.Append(Num(inset)).Append(' ').Append(Num(inset)).Append(' ').Append(Num(rw)).Append(' ').Append(Num(rh)).Append(" re\n");
        sb.Append(fill is not null && stroke is not null ? "B\n"
            : fill is not null ? "f\n"
            : "S\n");
        sb.Append('Q');

        AttachNormalAppearance(doc, annot, w, h, sb.ToString());
    }

    private static void AttachNormalAppearance(PdfDocument doc, PdfDictionary annot, double w, double h, string content)
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

    private static string Num(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
}
