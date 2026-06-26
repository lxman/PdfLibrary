using System.Numerics;
using PdfLibrary.Rendering;

namespace PdfLibrary.Tests.Rendering;

public class PathBuilderTransformTests
{
    [Fact]
    public void Transform_AppliesMatrixToEverySegment_AndLeavesOriginalUnchanged()
    {
        var src = new PathBuilder();
        src.MoveTo(1, 0);
        src.LineTo(0, 1);
        src.CurveTo(1, 1, 2, 2, 3, 3);
        src.ClosePath();

        // scale x2, translate (+10, +20)
        var m = new Matrix3x2(2, 0, 0, 2, 10, 20);
        IPathBuilder dst = ((IPathBuilder)src).Transform(m);

        IReadOnlyList<PathSegment> s = dst.Segments;
        Assert.Equal(new MoveToSegment(12, 20), s[0]);   // (1,0) -> (2+10, 0+20)
        Assert.Equal(new LineToSegment(10, 22), s[1]);   // (0,1) -> (0+10, 2+20)
        Assert.Equal(new CurveToSegment(12, 22, 14, 24, 16, 26), s[2]);
        Assert.IsType<ClosePathSegment>(s[3]);

        // original untouched
        Assert.Equal(new MoveToSegment(1, 0), src.Segments[0]);
    }
}
