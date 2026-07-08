using PdfLibrary.Rendering;

namespace PdfLibrary.Tests.Rendering;

public class PathBuilderSegmentsTests
{
    [Fact]
    public void Segments_AreReadable_ThroughTheInterface()
    {
        IPathBuilder path = new PathBuilder();
        path.MoveTo(1, 2);
        path.LineTo(3, 4);
        path.CurveTo(5, 6, 7, 8, 9, 10);
        path.ClosePath();

        IReadOnlyList<PathSegment> segments = path.Segments;

        Assert.Equal(4, segments.Count);
        Assert.IsType<MoveToSegment>(segments[0]);
        Assert.IsType<LineToSegment>(segments[1]);
        Assert.IsType<CurveToSegment>(segments[2]);
        Assert.IsType<ClosePathSegment>(segments[3]);
    }

    [Fact]
    public void CurveToV_UsesCurrentPointAsFirstControlPoint()
    {
        IPathBuilder path = new PathBuilder();
        path.MoveTo(10, 10);
        path.CurveToV(20, 20, 30, 30);   // first control point = current point (10,10)

        CurveToSegment seg = Assert.IsType<CurveToSegment>(path.Segments[1]);
        Assert.Equal(10, seg.X1);
        Assert.Equal(10, seg.Y1);
        Assert.Equal(20, seg.X2);
        Assert.Equal(20, seg.Y2);
        Assert.Equal(30, seg.X3);
        Assert.Equal(30, seg.Y3);
    }
}
