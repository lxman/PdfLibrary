namespace PdfLibrary.Editing.Stamping;

/// <summary>Describes where/how a stamp's BBox is placed onto a page, and computes the cm matrices.</summary>
internal sealed class StampPlacement
{
    internal enum Kind { Identity, At, Center, TopLeft, TopRight, BottomLeft, BottomRight, Diagonal, Tiled }

    internal Kind Preset { get; private init; } = Kind.Identity;
    internal double X { get; private init; }
    internal double Y { get; private init; }
    internal double ScaleFactor { get; set; } = 1.0;
    internal double RotateDeg { get; set; }
    internal double TileSpacing { get; private init; }
    internal double Margin { get; init; } = 36.0;

    internal static StampPlacement Identity() => new() { Preset = Kind.Identity };
    internal static StampPlacement At(double x, double y) => new() { Preset = Kind.At, X = x, Y = y };
    internal static StampPlacement Center() => new() { Preset = Kind.Center };
    internal static StampPlacement TopLeft() => new() { Preset = Kind.TopLeft };
    internal static StampPlacement TopRight() => new() { Preset = Kind.TopRight };
    internal static StampPlacement BottomLeft() => new() { Preset = Kind.BottomLeft };
    internal static StampPlacement BottomRight() => new() { Preset = Kind.BottomRight };
    internal static StampPlacement Diagonal() => new() { Preset = Kind.Diagonal };
    internal static StampPlacement Tiled(double spacing) => new() { Preset = Kind.Tiled, TileSpacing = spacing };

    /// <summary>Returns one or more [a,b,c,d,e,f] cm matrices placing the bbox onto a page of the given size.</summary>
    internal IReadOnlyList<double[]> ComputeMatrices(double pageW, double pageH, double bboxW, double bboxH)
    {
        double s = ScaleFactor;
        double theta = RotateDeg * Math.PI / 180.0;

        switch (Preset)
        {
            case Kind.Identity:    return [BottomLeftMatrix(0, 0, s, theta)];
            case Kind.At:          return [BottomLeftMatrix(X, Y, s, theta)];
            case Kind.Center:      return [CenterMatrix(pageW / 2, pageH / 2, s, theta, bboxW, bboxH)];
            case Kind.TopLeft:     return [CenterMatrix(Margin + s * bboxW / 2, pageH - Margin - s * bboxH / 2, s, theta, bboxW, bboxH)];
            case Kind.TopRight:    return [CenterMatrix(pageW - Margin - s * bboxW / 2, pageH - Margin - s * bboxH / 2, s, theta, bboxW, bboxH)];
            case Kind.BottomLeft:  return [CenterMatrix(Margin + s * bboxW / 2, Margin + s * bboxH / 2, s, theta, bboxW, bboxH)];
            case Kind.BottomRight: return [CenterMatrix(pageW - Margin - s * bboxW / 2, Margin + s * bboxH / 2, s, theta, bboxW, bboxH)];
            case Kind.Diagonal:
            {
                double diag = Math.Sqrt(pageW * pageW + pageH * pageH);
                double fit = bboxW > 0 ? 0.85 * diag / bboxW : 1.0;
                double angle = Math.Atan2(pageH, pageW);
                return [CenterMatrix(pageW / 2, pageH / 2, s * fit, angle, bboxW, bboxH)];
            }
            case Kind.Tiled:
            {
                var list = new List<double[]>();
                double step = TileSpacing;
                for (double y = 0; y < pageH; y += step)
                    for (double x = 0; x < pageW; x += step)
                        list.Add(BottomLeftMatrix(x, y, s, theta));
                return list;
            }
            default: return [BottomLeftMatrix(0, 0, s, theta)];
        }
    }

    private static double[] BottomLeftMatrix(double x, double y, double s, double theta)
    {
        double cos = Math.Cos(theta), sin = Math.Sin(theta);
        return [s * cos, s * sin, -s * sin, s * cos, x, y];
    }

    private static double[] CenterMatrix(double cx, double cy, double s, double theta, double bboxW, double bboxH)
    {
        double cos = Math.Cos(theta), sin = Math.Sin(theta);
        double a = s * cos, b = s * sin, c = -s * sin, d = s * cos;
        double hw = bboxW / 2, hh = bboxH / 2;
        double e = cx - (a * hw + c * hh);
        double f = cy - (b * hw + d * hh);
        return [a, b, c, d, e, f];
    }
}
