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
        if (_currentPoint == null)
            throw new InvalidOperationException("Cannot add line without current point. Use MoveTo first.");

        _segments.Add(new LineToSegment(x, y));
        _currentPoint = (x, y);
    }

    public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        if (_currentPoint == null)
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
        if (_subpathStart == null)
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

public record MoveToSegment(double X, double Y) : PathSegment;
public record LineToSegment(double X, double Y) : PathSegment;
public record CurveToSegment(double X1, double Y1, double X2, double Y2, double X3, double Y3) : PathSegment;
public record ClosePathSegment : PathSegment;
