using FontParser.Tables.Cff;
using SkiaSharp;
using CffGlyphOutline = FontParser.Tables.Cff.GlyphOutline;

namespace PdfLibrary.Fonts.Embedded;

/// <summary>
/// Converts glyph outlines to SkiaSharp SKPath for rendering.
/// Supports both TrueType (quadratic Bezier) and CFF (cubic Bezier) outlines.
/// </summary>
public class GlyphToSKPathConverter
{
    /// <summary>
    /// Convert a glyph outline to an SKPath suitable for rendering
    /// </summary>
    /// <param name="outline">The glyph outline to convert</param>
    /// <param name="fontSize">Font size in points</param>
    /// <param name="unitsPerEm">Units per em from the font's head table</param>
    /// <returns>SKPath representing the glyph shape</returns>
    public SKPath ConvertToPath(GlyphOutline outline, float fontSize, ushort unitsPerEm)
    {
        if (outline is null)
            throw new ArgumentNullException(nameof(outline));

        if (unitsPerEm == 0)
            throw new ArgumentException("Units per em cannot be zero", nameof(unitsPerEm));

        var path = new SKPath();
        float scale = fontSize / unitsPerEm;

        foreach (GlyphContour contour in outline.Contours)
        {
            if (contour.Points.Count == 0)
                continue;

            ProcessContour(path, contour, scale);
        }

        return path;
    }

    /// <summary>
    /// Convert a CFF glyph outline to an SKPath suitable for rendering.
    /// CFF uses cubic Bezier curves unlike TrueType's quadratic curves.
    /// </summary>
    /// <param name="outline">The CFF glyph outline to convert</param>
    /// <param name="fontSize">Font size in points</param>
    /// <param name="unitsPerEm">Units per em from the font's head table</param>
    /// <returns>SKPath representing the glyph shape</returns>
    public SKPath ConvertCffToPath(CffGlyphOutline outline, float fontSize, ushort unitsPerEm)
    {
        if (outline is null)
            throw new ArgumentNullException(nameof(outline));

        if (unitsPerEm == 0)
            throw new ArgumentException("Units per em cannot be zero", nameof(unitsPerEm));

        var path = new SKPath();
        float scale = fontSize / unitsPerEm;

        foreach (PathCommand command in outline.Commands)
        {
            switch (command)
            {
                case MoveToCommand moveTo:
                    SKPoint movePoint = ScalePoint(moveTo.Point.X, moveTo.Point.Y, scale);
                    path.MoveTo(movePoint);
                    break;

                case LineToCommand lineTo:
                    SKPoint linePoint = ScalePoint(lineTo.Point.X, lineTo.Point.Y, scale);
                    path.LineTo(linePoint);
                    break;

                case CubicBezierCommand cubic:
                    SKPoint c1 = ScalePoint(cubic.Control1.X, cubic.Control1.Y, scale);
                    SKPoint c2 = ScalePoint(cubic.Control2.X, cubic.Control2.Y, scale);
                    SKPoint end = ScalePoint(cubic.EndPoint.X, cubic.EndPoint.Y, scale);
                    path.CubicTo(c1, c2, end);
                    break;

                case ClosePathCommand:
                    path.Close();
                    break;
            }
        }

        return path;
    }

    /// <summary>
    /// Process a single contour and add it to the path
    /// </summary>
    private void ProcessContour(SKPath path, GlyphContour contour, float scale)
    {
        List<ContourPoint> points = contour.Points;
        if (points.Count == 0)
            return;

        // Find the first on-curve point to start the contour
        int startIndex = FindFirstOnCurvePoint(points);
        if (startIndex == -1)
        {
            // All points are off-curve - shouldn't happen in valid TrueType fonts
            // but handle gracefully by starting at the midpoint between first two
            if (points.Count < 2)
                return; // Can't process contour with < 2 points

            ContourPoint p0 = points[0];
            ContourPoint p1 = points[1];
            var midX = (short)((p0.X + p1.X) / 2);
            var midY = (short)((p0.Y + p1.Y) / 2);
            SKPoint midPoint = ScalePoint(midX, midY, scale);
            path.MoveTo(midPoint);
            startIndex = 0;
        }
        else
        {
            SKPoint startPoint = ScalePoint(points[startIndex].X, points[startIndex].Y, scale);
            path.MoveTo(startPoint);
        }

        // Process points in order, wrapping around to handle the contour as a loop
        int count = points.Count;
        for (var i = 1; i <= count; i++)
        {
            int currentIndex = (startIndex + i) % count;
            int prevIndex = (startIndex + i - 1) % count;

            ContourPoint currentPoint = points[currentIndex];
            ContourPoint prevPoint = points[prevIndex];

            if (currentPoint.OnCurve)
            {
                // Line segment to on-curve point
                SKPoint p = ScalePoint(currentPoint.X, currentPoint.Y, scale);
                path.LineTo(p);
            }
            else
            {
                // Off-curve point - this is a control point for a quadratic Bezier curve
                int nextIndex = (currentIndex + 1) % count;
                ContourPoint nextPoint = points[nextIndex];

                SKPoint controlPoint = ScalePoint(currentPoint.X, currentPoint.Y, scale);
                SKPoint endPoint;

                if (nextPoint.OnCurve)
                {
                    // Next point is on-curve, use it as the end point
                    endPoint = ScalePoint(nextPoint.X, nextPoint.Y, scale);
                    i++; // Skip the next point since we've already used it
                }
                else
                {
                    // Next point is also off-curve
                    // TrueType feature: implied on-curve point between two consecutive off-curve points
                    // The implied point is at the midpoint
                    var impliedX = (short)((currentPoint.X + nextPoint.X) / 2);
                    var impliedY = (short)((currentPoint.Y + nextPoint.Y) / 2);
                    endPoint = ScalePoint(impliedX, impliedY, scale);
                    // Don't skip next point - it will be processed in the next iteration
                }

                path.QuadTo(controlPoint, endPoint);
            }
        }

        // Close the contour
        path.Close();
    }

    /// <summary>
    /// Find the index of the first on-curve point in the contour
    /// Returns -1 if no on-curve points exist
    /// </summary>
    private int FindFirstOnCurvePoint(List<ContourPoint> points)
    {
        for (var i = 0; i < points.Count; i++)
        {
            if (points[i].OnCurve)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Scale a point from font units to pixels and flip the Y-axis
    /// TrueType uses Y-up coordinate system, SkiaSharp uses Y-down
    /// </summary>
    private SKPoint ScalePoint(double x, double y, float scale)
    {
        return new SKPoint(
            (float)(x * scale),
            (float)(-y * scale)  // Flip Y-axis: TrueType Y-up → SkiaSharp Y-down
        );
    }
}
