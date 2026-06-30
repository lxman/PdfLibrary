using System.Globalization;
using System.Text;
using PdfLibrary.Builder;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing.Forms;

namespace PdfLibrary.Editing.Annotations;

/// <summary>
/// Builds the graphics-operator content string for a markup annotation's <c>/AP /N</c> Form-XObject,
/// in appearance BBox space <c>[0 0 w h]</c>. Shared by both serialization paths — the editing-add
/// generator (<see cref="AnnotationAppearanceGenerator"/>) and the builder writer — so the drawn
/// appearance is identical regardless of how the document was produced.
/// </summary>
internal static class AnnotationContentBuilder
{
    public static string Num(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    public static string Square(double w, double h, double lw, double[]? stroke, double[]? fill)
    {
        double inset = lw / 2.0;
        double rw = Math.Max(0, w - lw), rh = Math.Max(0, h - lw);
        var sb = new StringBuilder();
        sb.Append("q\n").Append(Num(lw)).Append(" w\n");
        AppendFillStroke(sb, stroke, fill);
        sb.Append(Num(inset)).Append(' ').Append(Num(inset)).Append(' ').Append(Num(rw)).Append(' ').Append(Num(rh)).Append(" re\n");
        sb.Append(PaintOp(stroke, fill)).Append("\nQ");
        return sb.ToString();
    }

    public static string Circle(double w, double h, double lw, double[]? stroke, double[]? fill)
    {
        double inset = lw / 2.0;
        double cx = w / 2.0, cy = h / 2.0;
        double rx = Math.Max(0, w / 2.0 - inset), ry = Math.Max(0, h / 2.0 - inset);
        double kx = rx * 0.5523, ky = ry * 0.5523;
        var sb = new StringBuilder();
        sb.Append("q\n").Append(Num(lw)).Append(" w\n");
        AppendFillStroke(sb, stroke, fill);
        sb.Append(Num(cx + rx)).Append(' ').Append(Num(cy)).Append(" m\n");
        sb.Append(Num(cx + rx)).Append(' ').Append(Num(cy + ky)).Append(' ').Append(Num(cx + kx)).Append(' ').Append(Num(cy + ry)).Append(' ').Append(Num(cx)).Append(' ').Append(Num(cy + ry)).Append(" c\n");
        sb.Append(Num(cx - kx)).Append(' ').Append(Num(cy + ry)).Append(' ').Append(Num(cx - rx)).Append(' ').Append(Num(cy + ky)).Append(' ').Append(Num(cx - rx)).Append(' ').Append(Num(cy)).Append(" c\n");
        sb.Append(Num(cx - rx)).Append(' ').Append(Num(cy - ky)).Append(' ').Append(Num(cx - kx)).Append(' ').Append(Num(cy - ry)).Append(' ').Append(Num(cx)).Append(' ').Append(Num(cy - ry)).Append(" c\n");
        sb.Append(Num(cx + kx)).Append(' ').Append(Num(cy - ry)).Append(' ').Append(Num(cx + rx)).Append(' ').Append(Num(cy - ky)).Append(' ').Append(Num(cx + rx)).Append(' ').Append(Num(cy)).Append(" c\n");
        sb.Append(PaintOp(stroke, fill)).Append("\nQ");
        return sb.ToString();
    }

    /// <summary>Line between local points (already translated into BBox space).</summary>
    public static string Line(double lw, double[] stroke, double x0, double y0, double x1, double y1)
    {
        var sb = new StringBuilder();
        sb.Append("q\n").Append(Num(lw)).Append(" w\n1 J\n");
        sb.Append(Num(stroke[0])).Append(' ').Append(Num(stroke[1])).Append(' ').Append(Num(stroke[2])).Append(" RG\n");
        sb.Append(Num(x0)).Append(' ').Append(Num(y0)).Append(" m\n");
        sb.Append(Num(x1)).Append(' ').Append(Num(y1)).Append(" l\nS\nQ");
        return sb.ToString();
    }

    /// <summary>Ink: one or more polylines, points already translated into BBox space.</summary>
    public static string Ink(double lw, double[] stroke, IEnumerable<IReadOnlyList<(double X, double Y)>> localPaths)
    {
        var sb = new StringBuilder();
        sb.Append("q\n").Append(Num(lw)).Append(" w\n1 J\n1 j\n");
        sb.Append(Num(stroke[0])).Append(' ').Append(Num(stroke[1])).Append(' ').Append(Num(stroke[2])).Append(" RG\n");
        foreach (IReadOnlyList<(double X, double Y)> path in localPaths)
        {
            for (int i = 0; i < path.Count; i++)
                sb.Append(Num(path[i].X)).Append(' ').Append(Num(path[i].Y)).Append(i == 0 ? " m\n" : " l\n");
        }
        sb.Append("S\nQ");
        return sb.ToString();
    }

    /// <summary>
    /// FreeText layout using font resource <paramref name="resName"/>. Honors the FreeText line
    /// separators in <paramref name="text"/> (CR, LF, or CRLF — ISO 32000-1 §12.7.3.3): each line is
    /// shown by its own operator with a leading-based advance, since <c>Tj</c> does not break lines.
    /// <paramref name="quadding"/> is 0=left, 1=center, 2=right; <paramref name="w"/> is the BBox width
    /// used for center/right justification. <paramref name="colorOps"/> is a fragment like "0 0 0 rg".
    /// </summary>
    public static string FreeText(double w, double h, string resName, double size, string colorOps,
        string text, int quadding)
    {
        const double pad = 2.0;
        double baseline = h - pad - size;
        string baseFont = Standard14FontMap.BaseFont(resName);
        string[] lines = SplitLines(text);

        var sb = new StringBuilder();
        sb.Append("/Tx BMC\nq\nBT\n");
        sb.Append(colorOps).Append('\n');
        sb.Append('/').Append(resName).Append(' ').Append(Num(size)).Append(" Tf\n");

        if (lines.Length <= 1)
        {
            // Single line: keep the original Tm + one-Tj shape.
            string only = lines.Length == 0 ? string.Empty : lines[0];
            double x = LineStartX(only, quadding, w, baseFont, size, pad);
            sb.Append("1 0 0 1 ").Append(Num(x)).Append(' ').Append(Num(baseline)).Append(" Tm\n");
            sb.Append(Show(only)).Append(" Tj\n");
        }
        else
        {
            double leading = size * 1.2;
            sb.Append(Num(leading)).Append(" TL\n");
            double prevX = 0;
            for (var i = 0; i < lines.Length; i++)
            {
                double x = LineStartX(lines[i], quadding, w, baseFont, size, pad);
                if (i == 0)
                    sb.Append(Num(x)).Append(' ').Append(Num(baseline)).Append(" Td\n");
                else if (Math.Abs(x - prevX) < 1e-6)
                    sb.Append("T*\n");                                   // same X: pure line advance
                else
                    sb.Append(Num(x - prevX)).Append(' ').Append(Num(-leading)).Append(" Td\n");
                prevX = x;
                // A blank line consumes its advance (above) but shows nothing.
                if (lines[i].Length > 0)
                    sb.Append(Show(lines[i])).Append(" Tj\n");
            }
        }

        sb.Append("ET\nQ\nEMC");
        return sb.ToString();
    }

    /// <summary>Splits on CRLF/CR/LF, preserving empty lines so blank-line spacing is kept.</summary>
    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static string Show(string line) => PdfString.FromText(line).ToPdfString();

    private static double LineStartX(string line, int quadding, double w, string baseFont, double size, double pad)
    {
        if (quadding != 1 && quadding != 2) return pad; // left
        double tw = PdfFontMetrics.MeasureText(line, baseFont, size);
        return quadding == 1
            ? Math.Max(pad, (w - tw) / 2.0)     // center
            : Math.Max(pad, w - pad - tw);       // right
    }

    /// <summary>Highlight: Multiply-blend fill of the BBox; requires an /GShl ExtGState resource.</summary>
    public static string Highlight(double w, double h, double[] color) =>
        "q\n/GShl gs\n" +
        Num(color[0]) + " " + Num(color[1]) + " " + Num(color[2]) + " rg\n" +
        "0 0 " + Num(w) + " " + Num(h) + " re\nf\nQ";

    public static string NoteIcon(double w, double h) =>
        "q\n1 w\n0.4 0.4 0.4 RG\n1 0.9 0.4 rg\n" +
        "0.5 0.5 " + Num(w - 1) + " " + Num(h - 1) + " re\nB\n" +
        "0.3 0.3 0.3 RG\n0.6 w\n" +
        Num(w * 0.25) + " " + Num(h * 0.62) + " m " + Num(w * 0.75) + " " + Num(h * 0.62) + " l S\n" +
        Num(w * 0.25) + " " + Num(h * 0.42) + " m " + Num(w * 0.65) + " " + Num(h * 0.42) + " l S\nQ";

    private static void AppendFillStroke(StringBuilder sb, double[]? stroke, double[]? fill)
    {
        if (fill is not null)
            sb.Append(Num(fill[0])).Append(' ').Append(Num(fill[1])).Append(' ').Append(Num(fill[2])).Append(" rg\n");
        if (stroke is not null)
            sb.Append(Num(stroke[0])).Append(' ').Append(Num(stroke[1])).Append(' ').Append(Num(stroke[2])).Append(" RG\n");
    }

    private static string PaintOp(double[]? stroke, double[]? fill) =>
        fill is not null && stroke is not null ? "B" : fill is not null ? "f" : "S";
}
