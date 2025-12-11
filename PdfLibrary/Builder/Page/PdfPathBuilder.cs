namespace PdfLibrary.Builder.Page;

// ==================== CONTENT ELEMENT CLASSES ====================

// ==================== COLOR ====================

// ==================== PATH DRAWING ====================

/// <summary>
/// Fluent builder for creating paths with lines, curves, and shapes
/// </summary>
public class PdfPathBuilder
{
    private readonly PdfPageBuilder _pageBuilder;
    private readonly PdfPathContent _content;
    private double _currentX;
    private double _currentY;
    private double _startX;
    private double _startY;

    internal PdfPathBuilder(PdfPageBuilder pageBuilder, PdfPathContent content)
    {
        _pageBuilder = pageBuilder;
        _content = content;
    }

    /// <summary>
    /// Move to a new position without drawing
    /// </summary>
    public PdfPathBuilder MoveTo(double x, double y)
    {
        _content.Segments.Add(new PdfPathSegment { Type = PdfPathSegmentType.MoveTo, Points = [x, y] });
        _currentX = _startX = x;
        _currentY = _startY = y;
        return this;
    }

    /// <summary>
    /// Draw a line to the specified position
    /// </summary>
    public PdfPathBuilder LineTo(double x, double y)
    {
        _content.Segments.Add(new PdfPathSegment { Type = PdfPathSegmentType.LineTo, Points = [x, y] });
        _currentX = x;
        _currentY = y;
        return this;
    }

    /// <summary>
    /// Draw a cubic Bezier curve with two control points
    /// </summary>
    public PdfPathBuilder CurveTo(double cp1X, double cp1Y, double cp2X, double cp2Y, double endX, double endY)
    {
        _content.Segments.Add(new PdfPathSegment
        {
            Type = PdfPathSegmentType.CurveTo,
            Points = [cp1X, cp1Y, cp2X, cp2Y, endX, endY]
        });
        _currentX = endX;
        _currentY = endY;
        return this;
    }

    /// <summary>
    /// Draw a cubic Bezier curve where the first control point is the current point
    /// </summary>
    public PdfPathBuilder CurveToV(double cp2X, double cp2Y, double endX, double endY)
    {
        _content.Segments.Add(new PdfPathSegment
        {
            Type = PdfPathSegmentType.CurveToV,
            Points = [cp2X, cp2Y, endX, endY]
        });
        _currentX = endX;
        _currentY = endY;
        return this;
    }

    /// <summary>
    /// Draw a cubic Bezier curve where the second control point equals the endpoint
    /// </summary>
    public PdfPathBuilder CurveToY(double cp1X, double cp1Y, double endX, double endY)
    {
        _content.Segments.Add(new PdfPathSegment
        {
            Type = PdfPathSegmentType.CurveToY,
            Points = [cp1X, cp1Y, endX, endY]
        });
        _currentX = endX;
        _currentY = endY;
        return this;
    }

    /// <summary>
    /// Draw a quadratic Bezier curve (converted to cubic internally)
    /// </summary>
    public PdfPathBuilder QuadraticCurveTo(double cpX, double cpY, double endX, double endY)
    {
        // Convert quadratic to cubic Bezier
        // CP1 = P0 + 2/3 * (CP - P0) = P0 + 2/3*CP - 2/3*P0 = 1/3*P0 + 2/3*CP
        // CP2 = P2 + 2/3 * (CP - P2) = P2 + 2/3*CP - 2/3*P2 = 1/3*P2 + 2/3*CP
        double cp1X = _currentX + 2.0 / 3.0 * (cpX - _currentX);
        double cp1Y = _currentY + 2.0 / 3.0 * (cpY - _currentY);
        double cp2X = endX + 2.0 / 3.0 * (cpX - endX);
        double cp2Y = endY + 2.0 / 3.0 * (cpY - endY);

        return CurveTo(cp1X, cp1Y, cp2X, cp2Y, endX, endY);
    }

    /// <summary>
    /// Draw a circular arc (approximated with Bezier curves)
    /// </summary>
    public PdfPathBuilder Arc(double centerX, double centerY, double radius, double startAngle, double endAngle)
    {
        return EllipticalArc(centerX, centerY, radius, radius, startAngle, endAngle);
    }

    /// <summary>
    /// Draw an elliptical arc (approximated with Bezier curves)
    /// </summary>
    public PdfPathBuilder EllipticalArc(double centerX, double centerY, double radiusX, double radiusY,
        double startAngleDegrees, double endAngleDegrees)
    {
        double startRad = startAngleDegrees * Math.PI / 180.0;
        double endRad = endAngleDegrees * Math.PI / 180.0;

        // Normalize angles
        while (endRad < startRad)
            endRad += 2 * Math.PI;

        // Break arc into segments of at most 90 degrees for better approximation
        double totalAngle = endRad - startRad;
        var segments = (int)Math.Ceiling(Math.Abs(totalAngle) / (Math.PI / 2));
        double angleStep = totalAngle / segments;

        double currentAngle = startRad;

        // Move to start point
        double startX = centerX + radiusX * Math.Cos(currentAngle);
        double startY = centerY + radiusY * Math.Sin(currentAngle);
        MoveTo(startX, startY);

        for (var i = 0; i < segments; i++)
        {
            double nextAngle = currentAngle + angleStep;
            AddArcSegment(centerX, centerY, radiusX, radiusY, currentAngle, nextAngle);
            currentAngle = nextAngle;
        }

        return this;
    }

    private void AddArcSegment(double cx, double cy, double rx, double ry, double startAngle, double endAngle)
    {
        // Use the standard cubic Bezier approximation for circular arcs
        double angle = endAngle - startAngle;
        double alpha = Math.Sin(angle) * (Math.Sqrt(4 + 3 * Math.Pow(Math.Tan(angle / 2), 2)) - 1) / 3;

        double x1 = Math.Cos(startAngle);
        double y1 = Math.Sin(startAngle);
        double x2 = Math.Cos(endAngle);
        double y2 = Math.Sin(endAngle);

        double cp1X = cx + rx * (x1 - alpha * y1);
        double cp1Y = cy + ry * (y1 + alpha * x1);
        double cp2X = cx + rx * (x2 + alpha * y2);
        double cp2Y = cy + ry * (y2 - alpha * x2);
        double endX = cx + rx * x2;
        double endY = cy + ry * y2;

        CurveTo(cp1X, cp1Y, cp2X, cp2Y, endX, endY);
    }

    /// <summary>
    /// Add a rectangle to the path
    /// </summary>
    public PdfPathBuilder Rectangle(double x, double y, double width, double height)
    {
        _content.Segments.Add(new PdfPathSegment
        {
            Type = PdfPathSegmentType.Rectangle,
            Points = [x, y, width, height]
        });
        _currentX = x;
        _currentY = y;
        return this;
    }

    /// <summary>
    /// Add a rounded rectangle to the path
    /// </summary>
    public PdfPathBuilder RoundedRectangle(double x, double y, double width, double height, double cornerRadius)
    {
        double r = Math.Min(cornerRadius, Math.Min(width / 2, height / 2));

        // Start at top-left corner after the curve
        MoveTo(x + r, y);

        // Top edge and top-right corner
        LineTo(x + width - r, y);
        CurveTo(x + width - r * 0.45, y, x + width, y + r * 0.45, x + width, y + r);

        // Right edge and bottom-right corner
        LineTo(x + width, y + height - r);
        CurveTo(x + width, y + height - r * 0.45, x + width - r * 0.45, y + height, x + width - r, y + height);

        // Bottom edge and bottom-left corner
        LineTo(x + r, y + height);
        CurveTo(x + r * 0.45, y + height, x, y + height - r * 0.45, x, y + height - r);

        // Left edge and top-left corner
        LineTo(x, y + r);
        CurveTo(x, y + r * 0.45, x + r * 0.45, y, x + r, y);

        return ClosePath();
    }

    /// <summary>
    /// Add a circle to the path
    /// </summary>
    public PdfPathBuilder Circle(double centerX, double centerY, double radius)
    {
        return Ellipse(centerX, centerY, radius, radius);
    }

    /// <summary>
    /// Add an ellipse to the path
    /// </summary>
    public PdfPathBuilder Ellipse(double centerX, double centerY, double radiusX, double radiusY)
    {
        // Use Bezier approximation for ellipse (4 curves)
        const double k = 0.5522847498; // 4/3 * (sqrt(2) - 1)
        double kx = radiusX * k;
        double ky = radiusY * k;

        MoveTo(centerX + radiusX, centerY);
        CurveTo(centerX + radiusX, centerY + ky, centerX + kx, centerY + radiusY, centerX, centerY + radiusY);
        CurveTo(centerX - kx, centerY + radiusY, centerX - radiusX, centerY + ky, centerX - radiusX, centerY);
        CurveTo(centerX - radiusX, centerY - ky, centerX - kx, centerY - radiusY, centerX, centerY - radiusY);
        CurveTo(centerX + kx, centerY - radiusY, centerX + radiusX, centerY - ky, centerX + radiusX, centerY);

        return ClosePath();
    }

    /// <summary>
    /// Close the current subpath by drawing a line to the start point
    /// </summary>
    public PdfPathBuilder ClosePath()
    {
        _content.Segments.Add(new PdfPathSegment { Type = PdfPathSegmentType.ClosePath, Points = [] });
        _currentX = _startX;
        _currentY = _startY;
        return this;
    }

    // ==================== STYLING ====================

    /// <summary>
    /// Set fill color
    /// </summary>
    public PdfPathBuilder Fill(PdfColor color)
    {
        _content.FillColor = color;
        return this;
    }

    /// <summary>
    /// Set stroke color
    /// </summary>
    public PdfPathBuilder Stroke(PdfColor color, double lineWidth = 1)
    {
        _content.StrokeColor = color;
        _content.LineWidth = lineWidth;
        return this;
    }

    /// <summary>
    /// Set line width for stroke
    /// </summary>
    public PdfPathBuilder LineWidth(double width)
    {
        _content.LineWidth = width;
        return this;
    }

    /// <summary>
    /// Set line cap style
    /// </summary>
    public PdfPathBuilder LineCap(PdfLineCap cap)
    {
        _content.LineCap = cap;
        return this;
    }

    /// <summary>
    /// Set line join style
    /// </summary>
    public PdfPathBuilder LineJoin(PdfLineJoin join)
    {
        _content.LineJoin = join;
        return this;
    }

    /// <summary>
    /// Set miter limit for miter joins
    /// </summary>
    public PdfPathBuilder MiterLimit(double limit)
    {
        _content.MiterLimit = limit;
        return this;
    }

    /// <summary>
    /// Set dash pattern for strokes
    /// </summary>
    public PdfPathBuilder DashPattern(double[] pattern, double phase = 0)
    {
        _content.DashPattern = pattern;
        _content.DashPhase = phase;
        return this;
    }

    /// <summary>
    /// Set a simple dash pattern
    /// </summary>
    public PdfPathBuilder Dashed(double dashLength = 3, double gapLength = 3)
    {
        return DashPattern([dashLength, gapLength]);
    }

    /// <summary>
    /// Set a dotted pattern
    /// </summary>
    public PdfPathBuilder Dotted(double dotSize = 1, double gapSize = 2)
    {
        _content.LineCap = PdfLineCap.Round;
        return DashPattern([0, dotSize + gapSize]);
    }

    /// <summary>
    /// Set fill rule
    /// </summary>
    public PdfPathBuilder FillRule(PdfFillRule rule)
    {
        _content.FillRule = rule;
        return this;
    }

    /// <summary>
    /// Set fill opacity (0-1)
    /// </summary>
    public PdfPathBuilder FillOpacity(double opacity)
    {
        _content.FillOpacity = Math.Clamp(opacity, 0, 1);
        return this;
    }

    /// <summary>
    /// Set stroke opacity (0-1)
    /// </summary>
    public PdfPathBuilder StrokeOpacity(double opacity)
    {
        _content.StrokeOpacity = Math.Clamp(opacity, 0, 1);
        return this;
    }

    // ==================== TRANSFORMATIONS ====================

    /// <summary>
    /// Apply a transformation matrix to this path
    /// </summary>
    public PdfPathBuilder WithTransform(System.Numerics.Matrix3x2 matrix)
    {
        _content.Transform = matrix;
        return this;
    }

    /// <summary>
    /// Translate (move) this path by the specified offset
    /// </summary>
    public PdfPathBuilder Translate(double x, double y)
    {
        var translation = System.Numerics.Matrix3x2.CreateTranslation((float)x, (float)y);
        _content.Transform = _content.Transform.HasValue
            ? translation * _content.Transform.Value
            : translation;
        return this;
    }

    /// <summary>
    /// Scale this path by the specified factors
    /// </summary>
    public PdfPathBuilder Scale(double sx, double sy)
    {
        var scale = System.Numerics.Matrix3x2.CreateScale((float)sx, (float)sy);
        _content.Transform = _content.Transform.HasValue
            ? scale * _content.Transform.Value
            : scale;
        return this;
    }

    /// <summary>
    /// Scale this path uniformly
    /// </summary>
    public PdfPathBuilder Scale(double s)
    {
        return Scale(s, s);
    }

    /// <summary>
    /// Rotate this path by the specified angle in degrees
    /// </summary>
    public PdfPathBuilder Rotate(double angleDegrees)
    {
        double angleRadians = angleDegrees * Math.PI / 180.0;
        var rotation = System.Numerics.Matrix3x2.CreateRotation((float)angleRadians);
        _content.Transform = _content.Transform.HasValue
            ? rotation * _content.Transform.Value
            : rotation;
        return this;
    }

    // ==================== ADVANCED GRAPHICS STATE ====================

    /// <summary>
    /// Enable or disable fill overprint
    /// </summary>
    public PdfPathBuilder WithFillOverprint(bool enabled = true)
    {
        _content.FillOverprint = enabled;
        return this;
    }

    /// <summary>
    /// Enable or disable stroke overprint
    /// </summary>
    public PdfPathBuilder WithStrokeOverprint(bool enabled = true)
    {
        _content.StrokeOverprint = enabled;
        return this;
    }

    /// <summary>
    /// Set overprint mode (0 or 1)
    /// </summary>
    public PdfPathBuilder WithOverprintMode(int mode)
    {
        _content.OverprintMode = mode;
        return this;
    }

    /// <summary>
    /// Set blend mode
    /// </summary>
    public PdfPathBuilder WithBlendMode(string mode)
    {
        _content.BlendMode = mode;
        return this;
    }

    // ==================== CLIPPING ====================

    /// <summary>
    /// Use this path as a clipping path
    /// </summary>
    public PdfPathBuilder AsClippingPath()
    {
        _content.IsClippingPath = true;
        return this;
    }

    /// <summary>
    /// Return to page builder
    /// </summary>
    public PdfPageBuilder Done()
    {
        return _pageBuilder;
    }

    /// <summary>
    /// Implicit conversion back to page builder
    /// </summary>
    public static implicit operator PdfPageBuilder(PdfPathBuilder builder)
    {
        return builder._pageBuilder;
    }
}
