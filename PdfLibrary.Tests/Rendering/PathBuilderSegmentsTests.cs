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
}
