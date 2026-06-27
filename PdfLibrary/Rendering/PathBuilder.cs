using System.Numerics;

namespace PdfLibrary.Rendering;

/// <summary>
/// Default implementation of IPathBuilder that stores path segments
/// Platform renderers can convert this to their native path format
/// </summary>
public class PathBuilder : IPathBuilder
{
    private readonly List<PathSegment> _segments = [];
    private (double x, double y)? _currentPoint;
    private (double x, double y)? _subpathStart;

    public bool IsEmpty => _segments.Count == 0;

    public void MoveTo(double x, double y)
    {
        _segments.Add(new MoveToSegment(x, y));
        _currentPoint = (x, y);
        _subpathStart = (x, y);
    }

    public void LineTo(double x, double y)
    {
        if (_currentPoint is null)
            throw new InvalidOperationException("Cannot add line without current point. Use MoveTo first.");

        _segments.Add(new LineToSegment(x, y));
        _currentPoint = (x, y);
    }

    public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        if (_currentPoint is null)
            throw new InvalidOperationException("Cannot add curve without current point. Use MoveTo first.");

        _segments.Add(new CurveToSegment(x1, y1, x2, y2, x3, y3));
        _currentPoint = (x3, y3);
    }

    public void Rectangle(double x, double y, double width, double height)
    {
        // Rectangle is drawn as: moveto, lineto, lineto, lineto, closepath
        MoveTo(x, y);
        LineTo(x + width, y);
        LineTo(x + width, y + height);
        LineTo(x, y + height);
        ClosePath();
    }

    public void ClosePath()
    {
        if (_subpathStart is null)
            return;

        _segments.Add(new ClosePathSegment());
        _currentPoint = _subpathStart;
    }

    public void Clear()
    {
        _segments.Clear();
        _currentPoint = null;
        _subpathStart = null;
    }

    public IPathBuilder Clone()
    {
        var clone = new PathBuilder();
        clone._segments.AddRange(_segments);
        clone._currentPoint = _currentPoint;
        clone._subpathStart = _subpathStart;
        return clone;
    }

    public IPathBuilder Transform(Matrix3x2 matrix)
    {
        var result = new PathBuilder();
        foreach (PathSegment seg in _segments)
        {
            switch (seg)
            {
                case MoveToSegment m:
                {
                    Vector2 p = Vector2.Transform(new Vector2((float)m.X, (float)m.Y), matrix);
                    result.MoveTo(p.X, p.Y);
                    break;
                }
                case LineToSegment l:
                {
                    Vector2 p = Vector2.Transform(new Vector2((float)l.X, (float)l.Y), matrix);
                    result.LineTo(p.X, p.Y);
                    break;
                }
                case CurveToSegment c:
                {
                    Vector2 p1 = Vector2.Transform(new Vector2((float)c.X1, (float)c.Y1), matrix);
                    Vector2 p2 = Vector2.Transform(new Vector2((float)c.X2, (float)c.Y2), matrix);
                    Vector2 p3 = Vector2.Transform(new Vector2((float)c.X3, (float)c.Y3), matrix);
                    result.CurveTo(p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y);
                    break;
                }
                case ClosePathSegment:
                    result.ClosePath();
                    break;
            }
        }
        return result;
    }

    /// <summary>
    /// Gets all path segments for rendering
    /// </summary>
    public IReadOnlyList<PathSegment> Segments => _segments;

    /// <summary>
    /// Gets the current point in the path
    /// </summary>
    public (double x, double y)? CurrentPoint => _currentPoint;
}

/// <summary>
/// Base class for path segments
/// </summary>
public abstract record PathSegment;

/// <summary>A move-to path segment; sets the current point without drawing.</summary>
public record MoveToSegment(double X, double Y) : PathSegment;
/// <summary>A line-to path segment; draws a straight line from the current point to (X, Y).</summary>
public record LineToSegment(double X, double Y) : PathSegment;
/// <summary>A cubic Bezier curve segment; (X1,Y1) and (X2,Y2) are control points, (X3,Y3) is the end point.</summary>
public record CurveToSegment(double X1, double Y1, double X2, double Y2, double X3, double Y3) : PathSegment;
/// <summary>A close-path segment; closes the current sub-path by drawing a straight line back to the start point.</summary>
public record ClosePathSegment : PathSegment;
