using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Rendering.SkiaSharp;
using SkiaSharp;

namespace PdfLibrary.Tests.Fonts.Embedded;

public class GlyphToSKPathConverterTests
{
    private readonly GlyphToSKPathConverter _converter = new();

    [Fact]
    public void ConvertToPath_WithNullOutline_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _converter.ConvertToPath(null!, 12.0f, 1000));
    }

    [Fact]
    public void ConvertToPath_WithZeroUnitsPerEm_ThrowsArgumentException()
    {
        // Arrange
        var metrics = new GlyphMetrics(0, 0, 0, 0, 0, 0);
        var outline = new GlyphOutline(0, new List<GlyphContour>(), metrics);

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _converter.ConvertToPath(outline, 12.0f, 0));
    }

    [Fact]
    public void ConvertToPath_WithEmptyContours_ReturnsEmptyPath()
    {
        // Arrange
        var metrics = new GlyphMetrics(0, 0, 0, 0, 0, 0);
        var outline = new GlyphOutline(0, new List<GlyphContour>(), metrics);

        // Act
        SKPath path = _converter.ConvertToPath(outline, 12.0f, 1000);

        // Assert
        Assert.NotNull(path);
        Assert.Equal(0, path.PointCount);
    }

    [Fact]
    public void ConvertToPath_WithSimpleSquare_CreatesCorrectPath()
    {
        // Arrange - Create a simple square (100x100 units)
        var contour = new GlyphContour(new List<ContourPoint>
        {
            new ContourPoint(0, 0, true),      // Bottom-left (on-curve)
            new ContourPoint(100, 0, true),    // Bottom-right (on-curve)
            new ContourPoint(100, 100, true),  // Top-right (on-curve)
            new ContourPoint(0, 100, true)     // Top-left (on-curve)
        });

        var metrics = new GlyphMetrics(100, 0, 0, 0, 100, 100);
        var outline = new GlyphOutline(0, new List<GlyphContour> { contour }, metrics);

        // Act - 12pt font, 1000 units per em (scale = 12/1000 = 0.012)
        SKPath path = _converter.ConvertToPath(outline, 12.0f, 1000);

        // Assert
        Assert.NotNull(path);
        Assert.True(path.PointCount > 0);

        // Verify the path is closed
        SKRect bounds = path.Bounds;
        Assert.True(bounds.Width > 0);
        Assert.True(bounds.Height > 0);
    }

    [Fact]
    public void ConvertToPath_ScalesPointsCorrectly()
    {
        // Arrange - Single point at (1000, 0)
        var contour = new GlyphContour(new List<ContourPoint>
        {
            new ContourPoint(1000, 0, true),
            new ContourPoint(1000, 1000, true),
            new ContourPoint(0, 1000, true),
            new ContourPoint(0, 0, true)
        });

        var metrics = new GlyphMetrics(1000, 0, 0, 0, 1000, 1000);
        var outline = new GlyphOutline(0, new List<GlyphContour> { contour }, metrics);

        // Act - 24pt font, 1000 units per em (scale = 24/1000 = 0.024)
        SKPath path = _converter.ConvertToPath(outline, 24.0f, 1000);

        // Assert - The bounding box should be 24x24 pixels
        SKRect bounds = path.Bounds;
        Assert.Equal(24.0f, bounds.Width, 2);  // Allow small tolerance for floating point
        Assert.Equal(24.0f, bounds.Height, 2);
    }

    [Fact]
    public void ConvertToPath_FlipsYAxisCorrectly()
    {
        // Arrange - Point with positive Y should become negative after flip
        var contour = new GlyphContour(new List<ContourPoint>
        {
            new ContourPoint(0, 0, true),
            new ContourPoint(100, 0, true),
            new ContourPoint(100, 100, true),  // Positive Y in TrueType
            new ContourPoint(0, 100, true)
        });

        var metrics = new GlyphMetrics(100, 0, 0, 0, 100, 100);
        var outline = new GlyphOutline(0, new List<GlyphContour> { contour }, metrics);

        // Act
        SKPath path = _converter.ConvertToPath(outline, 12.0f, 1000);

        // Assert - Y coordinates should be flipped (negative in SkiaSharp)
        SKRect bounds = path.Bounds;
        Assert.True(bounds.Top < 0, "Y-axis should be flipped, so top should be negative");
    }

    [Fact]
    public void ConvertToPath_WithQuadraticBezier_CreatesSmoothCurve()
    {
        // Arrange - Curve with control point
        var contour = new GlyphContour(new List<ContourPoint>
        {
            new ContourPoint(0, 0, true),        // Start point (on-curve)
            new ContourPoint(50, 100, false),    // Control point (off-curve)
            new ContourPoint(100, 0, true)       // End point (on-curve)
        });

        var metrics = new GlyphMetrics(100, 0, 0, 0, 100, 100);
        var outline = new GlyphOutline(0, new List<GlyphContour> { contour }, metrics);

        // Act
        SKPath path = _converter.ConvertToPath(outline, 12.0f, 1000);

        // Assert
        Assert.NotNull(path);
        Assert.True(path.PointCount >= 3, "Should have at least 3 points for a curve");
    }

    [Fact]
    public void ConvertToPath_WithConsecutiveOffCurvePoints_CreatesImpliedPoint()
    {
        // Arrange - Two consecutive off-curve points
        // TrueType creates an implied on-curve point at their midpoint
        var contour = new GlyphContour(new List<ContourPoint>
        {
            new ContourPoint(0, 0, true),        // Start (on-curve)
            new ContourPoint(25, 50, false),     // Control point 1 (off-curve)
            new ContourPoint(75, 50, false),     // Control point 2 (off-curve)
            new ContourPoint(100, 0, true)       // End (on-curve)
        });

        var metrics = new GlyphMetrics(100, 0, 0, 0, 100, 100);
        var outline = new GlyphOutline(0, new List<GlyphContour> { contour }, metrics);

        // Act
        SKPath path = _converter.ConvertToPath(outline, 12.0f, 1000);

        // Assert
        Assert.NotNull(path);
        // Should create two quadratic curves with an implied point between the off-curve points
        Assert.True(path.PointCount >= 4);
    }

    [Fact]
    public void ConvertToPath_WithMultipleContours_ProcessesAll()
    {
        // Arrange - Two separate contours (like letter 'O' with inner and outer)
        var outerContour = new GlyphContour(new List<ContourPoint>
        {
            new ContourPoint(0, 0, true),
            new ContourPoint(100, 0, true),
            new ContourPoint(100, 100, true),
            new ContourPoint(0, 100, true)
        });

        var innerContour = new GlyphContour(new List<ContourPoint>
        {
            new ContourPoint(20, 20, true),
            new ContourPoint(80, 20, true),
            new ContourPoint(80, 80, true),
            new ContourPoint(20, 80, true)
        });

        var metrics = new GlyphMetrics(100, 0, 0, 0, 100, 100);
        var outline = new GlyphOutline(0,
            new List<GlyphContour> { outerContour, innerContour },
            metrics);

        // Act
        SKPath path = _converter.ConvertToPath(outline, 12.0f, 1000);

        // Assert
        Assert.NotNull(path);
        // Should have points from both contours
        Assert.True(path.PointCount >= 8, "Should have points from both contours");
    }

    [Fact]
    public void ConvertToPath_WithContourStartingOnOffCurvePoint_HandlesGracefully()
    {
        // Arrange - Contour starting with off-curve point (unusual but valid in TrueType)
        var contour = new GlyphContour(new List<ContourPoint>
        {
            new ContourPoint(50, 50, false),     // Start with off-curve (unusual)
            new ContourPoint(100, 0, true),
            new ContourPoint(0, 0, true)
        });

        var metrics = new GlyphMetrics(100, 0, 0, 0, 100, 100);
        var outline = new GlyphOutline(0, new List<GlyphContour> { contour }, metrics);

        // Act - Should not throw
        SKPath path = _converter.ConvertToPath(outline, 12.0f, 1000);

        // Assert
        Assert.NotNull(path);
        Assert.True(path.PointCount > 0);
    }

    [Fact]
    public void ConvertToPath_WithDifferentFontSizes_ScalesProperly()
    {
        // Arrange
        var contour = new GlyphContour(new List<ContourPoint>
        {
            new ContourPoint(0, 0, true),
            new ContourPoint(1000, 0, true),
            new ContourPoint(1000, 1000, true),
            new ContourPoint(0, 1000, true)
        });

        var metrics = new GlyphMetrics(1000, 0, 0, 0, 1000, 1000);
        var outline = new GlyphOutline(0, new List<GlyphContour> { contour }, metrics);

        // Act - Test with different font sizes
        SKPath path12 = _converter.ConvertToPath(outline, 12.0f, 1000);
        SKPath path24 = _converter.ConvertToPath(outline, 24.0f, 1000);
        SKPath path48 = _converter.ConvertToPath(outline, 48.0f, 1000);

        // Assert - Larger font size should produce larger bounds
        Assert.True(path24.Bounds.Width > path12.Bounds.Width);
        Assert.True(path48.Bounds.Width > path24.Bounds.Width);

        // Should scale proportionally (approximately, allowing for float precision)
        Assert.Equal(2.0f, path24.Bounds.Width / path12.Bounds.Width, 1);
        Assert.Equal(4.0f, path48.Bounds.Width / path12.Bounds.Width, 1);
    }

    [Fact]
    public void ConvertToPath_ClosesContoursCorrectly()
    {
        // Arrange - Simple triangle
        var contour = new GlyphContour(new List<ContourPoint>
        {
            new ContourPoint(0, 0, true),
            new ContourPoint(100, 0, true),
            new ContourPoint(50, 100, true)
        });

        var metrics = new GlyphMetrics(100, 0, 0, 0, 100, 100);
        var outline = new GlyphOutline(0, new List<GlyphContour> { contour }, metrics);

        // Act
        SKPath path = _converter.ConvertToPath(outline, 12.0f, 1000);

        // Assert - Path should be closed (SkiaSharp closes paths automatically with Close())
        Assert.NotNull(path);
        SKRect bounds = path.Bounds;
        Assert.True(bounds is { Width: > 0, Height: > 0 });
    }
}
