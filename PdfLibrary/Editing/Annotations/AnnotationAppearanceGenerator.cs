using System.Globalization;
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

    private static void GenerateCircle(PdfDocument doc, PdfDictionary annot)
    {
        if (!TryGetRect(annot, out double x0, out double y0, out double x1, out double y1)) return;
        double w = Math.Abs(x1 - x0), h = Math.Abs(y1 - y0);
        if (w <= 0 || h <= 0) return;

        double lw = GetBorderWidth(annot);
        double[]? stroke = GetColor(annot, "C");
        double[]? fill = GetColor(annot, "IC");

        // Ellipse inscribed in the inset box, via 4 Bézier quadrants (k = 0.5523).
        double inset = lw / 2.0;
        double cx = w / 2.0, cy = h / 2.0;
        double rx = Math.Max(0, w / 2.0 - inset), ry = Math.Max(0, h / 2.0 - inset);
        double kx = rx * 0.5523, ky = ry * 0.5523;

        var sb = new StringBuilder();
        sb.Append("q\n").Append(Num(lw)).Append(" w\n");
        if (fill is not null)
            sb.Append(Num(fill[0])).Append(' ').Append(Num(fill[1])).Append(' ').Append(Num(fill[2])).Append(" rg\n");
        if (stroke is not null)
            sb.Append(Num(stroke[0])).Append(' ').Append(Num(stroke[1])).Append(' ').Append(Num(stroke[2])).Append(" RG\n");
        sb.Append(Num(cx + rx)).Append(' ').Append(Num(cy)).Append(" m\n");
        sb.Append(Num(cx + rx)).Append(' ').Append(Num(cy + ky)).Append(' ').Append(Num(cx + kx)).Append(' ').Append(Num(cy + ry)).Append(' ').Append(Num(cx)).Append(' ').Append(Num(cy + ry)).Append(" c\n");
        sb.Append(Num(cx - kx)).Append(' ').Append(Num(cy + ry)).Append(' ').Append(Num(cx - rx)).Append(' ').Append(Num(cy + ky)).Append(' ').Append(Num(cx - rx)).Append(' ').Append(Num(cy)).Append(" c\n");
        sb.Append(Num(cx - rx)).Append(' ').Append(Num(cy - ky)).Append(' ').Append(Num(cx - kx)).Append(' ').Append(Num(cy - ry)).Append(' ').Append(Num(cx)).Append(' ').Append(Num(cy - ry)).Append(" c\n");
        sb.Append(Num(cx + kx)).Append(' ').Append(Num(cy - ry)).Append(' ').Append(Num(cx + rx)).Append(' ').Append(Num(cy - ky)).Append(' ').Append(Num(cx + rx)).Append(' ').Append(Num(cy)).Append(" c\n");
        sb.Append(fill is not null && stroke is not null ? "B\n" : fill is not null ? "f\n" : "S\n");
        sb.Append('Q');

        AttachNormalAppearance(doc, annot, w, h, sb.ToString());
    }

    private static void GenerateLine(PdfDocument doc, PdfDictionary annot)
    {
        if (!TryGetRect(annot, out double rx0, out double ry0, out double rx1, out double ry1)) return;
        double w = Math.Abs(rx1 - rx0), h = Math.Abs(ry1 - ry0);
        if (w <= 0 || h <= 0) return;
        if (annot.Get(new PdfName("L")) is not PdfArray { Count: >= 4 } l) return;

        double ox = Math.Min(rx0, rx1), oy = Math.Min(ry0, ry1);
        double lx0 = ToDouble(l[0]) - ox, ly0 = ToDouble(l[1]) - oy;
        double lx1 = ToDouble(l[2]) - ox, ly1 = ToDouble(l[3]) - oy;

        double lw = GetBorderWidth(annot);
        double[]? stroke = GetColor(annot, "C") ?? [0, 0, 0];

        var sb = new StringBuilder();
        sb.Append("q\n").Append(Num(lw)).Append(" w\n1 J\n");
        sb.Append(Num(stroke[0])).Append(' ').Append(Num(stroke[1])).Append(' ').Append(Num(stroke[2])).Append(" RG\n");
        sb.Append(Num(lx0)).Append(' ').Append(Num(ly0)).Append(" m\n");
        sb.Append(Num(lx1)).Append(' ').Append(Num(ly1)).Append(" l\nS\nQ");

        AttachNormalAppearance(doc, annot, w, h, sb.ToString());
    }

    private static void GenerateInk(PdfDocument doc, PdfDictionary annot)
    {
        if (!TryGetRect(annot, out double rx0, out double ry0, out double rx1, out double ry1)) return;
        double w = Math.Abs(rx1 - rx0), h = Math.Abs(ry1 - ry0);
        if (w <= 0 || h <= 0) return;
        if (annot.Get(new PdfName("InkList")) is not PdfArray inkList) return;

        double ox = Math.Min(rx0, rx1), oy = Math.Min(ry0, ry1);
        double lw = GetBorderWidth(annot);
        double[]? stroke = GetColor(annot, "C") ?? [0, 0, 0];

        var sb = new StringBuilder();
        sb.Append("q\n").Append(Num(lw)).Append(" w\n1 J\n1 j\n");
        sb.Append(Num(stroke[0])).Append(' ').Append(Num(stroke[1])).Append(' ').Append(Num(stroke[2])).Append(" RG\n");
        foreach (PdfObject pathObj in inkList)
        {
            if (pathObj is not PdfArray pts || pts.Count < 4) continue;
            for (int i = 0; i + 1 < pts.Count; i += 2)
            {
                double px = ToDouble(pts[i]) - ox, py = ToDouble(pts[i + 1]) - oy;
                sb.Append(Num(px)).Append(' ').Append(Num(py)).Append(i == 0 ? " m\n" : " l\n");
            }
        }
        sb.Append("S\nQ");

        AttachNormalAppearance(doc, annot, w, h, sb.ToString());
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

        const double pad = 2.0;
        double baseline = h - pad - size;
        string showToken = PdfString.FromText(text).ToPdfString();

        string content =
            "/Tx BMC\nq\nBT\n" +
            da.ColorOps + "\n" +
            "/" + resName + " " + Num(size) + " Tf\n" +
            "1 0 0 1 " + Num(pad) + " " + Num(baseline) + " Tm\n" +
            showToken + " Tj\n" +
            "ET\nQ\nEMC";

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

        double[] color = GetColor(annot, "C") ?? [1, 1, 0]; // default yellow
        string content =
            "q\n/GShl gs\n" +
            Num(color[0]) + " " + Num(color[1]) + " " + Num(color[2]) + " rg\n" +
            "0 0 " + Num(w) + " " + Num(h) + " re\nf\nQ";

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

        // A simple "comment" icon: a yellow rounded box with a grey border and two text lines.
        string content =
            "q\n1 w\n0.4 0.4 0.4 RG\n1 0.9 0.4 rg\n" +
            "0.5 0.5 " + Num(w - 1) + " " + Num(h - 1) + " re\nB\n" +
            "0.3 0.3 0.3 RG\n0.6 w\n" +
            Num(w * 0.25) + " " + Num(h * 0.62) + " m " + Num(w * 0.75) + " " + Num(h * 0.62) + " l S\n" +
            Num(w * 0.25) + " " + Num(h * 0.42) + " m " + Num(w * 0.65) + " " + Num(h * 0.42) + " l S\nQ";

        AttachNormalAppearance(doc, annot, w, h, content);
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

    private static string Num(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
}
