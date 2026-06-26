using FontParser.Tables.Cff;
using PdfLibrary.Fonts.Embedded;
using CffGlyphOutline = FontParser.Tables.Cff.GlyphOutline;

namespace PdfLibrary.Rendering;

/// <summary>
/// Converts a glyph outline to an <see cref="IPathBuilder"/> in glyph space (scaled by
/// fontSize/unitsPerEm and Y-flipped from font Y-up to render Y-down). Cubic-only: TrueType
/// quadratics are degree-elevated to exact cubics. No fill rule is set on the path — glyph
/// fills use even-odd, supplied by the caller at FillPath time.
/// </summary>
internal static class GlyphOutlineToPath
{
    internal static IPathBuilder FromTrueType(PdfLibrary.Fonts.Embedded.GlyphOutline outline, float fontSize, ushort unitsPerEm)
    {
        if (outline is null) throw new ArgumentNullException(nameof(outline));
        if (unitsPerEm == 0) throw new ArgumentException("Units per em cannot be zero", nameof(unitsPerEm));

        var pb = new PathBuilder();
        float scale = fontSize / unitsPerEm;

        foreach (GlyphContour contour in outline.Contours)
        {
            if (contour.Points.Count == 0) continue;
            ProcessContour(pb, contour, scale);
        }

        return pb;
    }

    internal static IPathBuilder FromCff(CffGlyphOutline outline, float fontSize, ushort unitsPerEm)
    {
        if (outline is null) throw new ArgumentNullException(nameof(outline));
        if (unitsPerEm == 0) throw new ArgumentException("Units per em cannot be zero", nameof(unitsPerEm));

        var pb = new PathBuilder();
        float scale = fontSize / unitsPerEm;

        foreach (PathCommand command in outline.Commands)
        {
            switch (command)
            {
                case MoveToCommand m:
                {
                    (double x, double y) = ScalePoint(m.Point.X, m.Point.Y, scale);
                    pb.MoveTo(x, y);
                    break;
                }
                case LineToCommand l:
                {
                    (double x, double y) = ScalePoint(l.Point.X, l.Point.Y, scale);
                    pb.LineTo(x, y);
                    break;
                }
                case CubicBezierCommand c:
                {
                    (double c1x, double c1y) = ScalePoint(c.Control1.X, c.Control1.Y, scale);
                    (double c2x, double c2y) = ScalePoint(c.Control2.X, c.Control2.Y, scale);
                    (double ex, double ey) = ScalePoint(c.EndPoint.X, c.EndPoint.Y, scale);
                    pb.CurveTo(c1x, c1y, c2x, c2y, ex, ey);
                    break;
                }
                case ClosePathCommand:
                    pb.ClosePath();
                    break;
            }
        }

        return pb;
    }

    private static void ProcessContour(PathBuilder pb, GlyphContour contour, float scale)
    {
        List<ContourPoint> points = contour.Points;
        if (points.Count == 0) return;

        int startIndex = FindFirstOnCurvePoint(points);
        double curX, curY;

        if (startIndex == -1)
        {
            // All off-curve: start at the midpoint of the first two points.
            if (points.Count < 2) return;
            ContourPoint p0 = points[0], p1 = points[1];
            var midX = (short)((p0.X + p1.X) / 2);
            var midY = (short)((p0.Y + p1.Y) / 2);
            (curX, curY) = ScalePoint(midX, midY, scale);
            pb.MoveTo(curX, curY);
            startIndex = 0;
        }
        else
        {
            (curX, curY) = ScalePoint(points[startIndex].X, points[startIndex].Y, scale);
            pb.MoveTo(curX, curY);
        }

        int count = points.Count;
        for (var i = 1; i <= count; i++)
        {
            int currentIndex = (startIndex + i) % count;
            ContourPoint currentPoint = points[currentIndex];

            if (currentPoint.OnCurve)
            {
                (double px, double py) = ScalePoint(currentPoint.X, currentPoint.Y, scale);
                pb.LineTo(px, py);
                curX = px; curY = py;
            }
            else
            {
                int nextIndex = (currentIndex + 1) % count;
                ContourPoint nextPoint = points[nextIndex];
                (double ctrlX, double ctrlY) = ScalePoint(currentPoint.X, currentPoint.Y, scale);

                double endX, endY;
                if (nextPoint.OnCurve)
                {
                    (endX, endY) = ScalePoint(nextPoint.X, nextPoint.Y, scale);
                    i++; // consumed the next on-curve point
                }
                else
                {
                    // Implied on-curve point midway between two consecutive off-curve points.
                    var impliedX = (short)((currentPoint.X + nextPoint.X) / 2);
                    var impliedY = (short)((currentPoint.Y + nextPoint.Y) / 2);
                    (endX, endY) = ScalePoint(impliedX, impliedY, scale);
                }

                // Elevate the quadratic (curX,curY)->(ctrl)->(end) to an exact cubic.
                double c1x = curX + 2.0 / 3.0 * (ctrlX - curX);
                double c1y = curY + 2.0 / 3.0 * (ctrlY - curY);
                double c2x = endX + 2.0 / 3.0 * (ctrlX - endX);
                double c2y = endY + 2.0 / 3.0 * (ctrlY - endY);
                pb.CurveTo(c1x, c1y, c2x, c2y, endX, endY);
                curX = endX; curY = endY;
            }
        }

        pb.ClosePath();
    }

    private static int FindFirstOnCurvePoint(List<ContourPoint> points)
    {
        for (var i = 0; i < points.Count; i++)
            if (points[i].OnCurve) return i;
        return -1;
    }

    // Scale font units to render units and flip Y (font Y-up -> render Y-down).
    private static (double x, double y) ScalePoint(double x, double y, float scale)
        => (x * scale, -y * scale);
}
