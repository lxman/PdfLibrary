using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Rendering;

namespace PdfLibrary.Tests.Rendering;

public class GlyphOutlineToPathTests
{
    private static GlyphMetrics Metrics() => new(0, 0, 0, 0, 0, 0);

    // A triangle of three on-curve points; unitsPerEm == fontSize so scale == 1.
    [Fact]
    public void TrueType_OnCurveTriangle_ScalesAndYFlips()
    {
        var contour = new GlyphContour(
        [
            new ContourPoint(0, 0, onCurve: true),
            new ContourPoint(100, 0, onCurve: true),
            new ContourPoint(50, 100, onCurve: true),
        ]);
        var outline = new GlyphOutline(0, [contour], Metrics());

        IPathBuilder path = GlyphOutlineToPath.FromTrueType(outline, fontSize: 100, unitsPerEm: 100);
        IReadOnlyList<PathSegment> s = path.Segments;

        Assert.IsType<MoveToSegment>(s[0]);
        var m = (MoveToSegment)s[0];
        Assert.Equal(0, m.X, 3);
        Assert.Equal(0, m.Y, 3);
        // Y is flipped: (50,100) -> (50,-100)
        Assert.Contains(s, seg => seg is LineToSegment { X: 50, Y: -100 });
        Assert.IsType<ClosePathSegment>(s[^1]);
    }

    // One off-curve control point between two on-curve points -> one elevated cubic.
    [Fact]
    public void TrueType_QuadraticControl_ElevatesToCubic()
    {
        var contour = new GlyphContour(
        [
            new ContourPoint(0, 0, onCurve: true),     // P0
            new ContourPoint(50, 100, onCurve: false),  // Q (control)
            new ContourPoint(100, 0, onCurve: true),    // P1
        ]);
        var outline = new GlyphOutline(0, [contour], Metrics());

        IPathBuilder path = GlyphOutlineToPath.FromTrueType(outline, fontSize: 100, unitsPerEm: 100);
        IReadOnlyList<PathSegment> s = path.Segments;

        CurveToSegment c = Assert.IsType<CurveToSegment>(s[1]);
        // scaled+flipped: P0=(0,0) Q=(50,-100) P1=(100,0); cubic C1=P0+2/3(Q-P0), C2=P1+2/3(Q-P1)
        Assert.Equal(33.333, c.X1, 2);
        Assert.Equal(-66.667, c.Y1, 2);
        Assert.Equal(66.667, c.X2, 2);
        Assert.Equal(-66.667, c.Y2, 2);
        Assert.Equal(100, c.X3, 3);
        Assert.Equal(0, c.Y3, 3);
    }
}
