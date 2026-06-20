using PdfLibrary.Editing.Stamping;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class StampPlacementTests
{
    [Fact]
    public void Identity_PlacesBBoxAtOrigin()
    {
        double[] m = StampPlacement.Identity().ComputeMatrices(600, 800, 200, 100)[0];
        Assert.Equal(new[] { 1.0, 0, 0, 1, 0, 0 }, m);
    }

    [Fact]
    public void At_PlacesBottomLeftAtPoint()
    {
        double[] m = StampPlacement.At(50, 60).ComputeMatrices(600, 800, 200, 100)[0];
        Assert.Equal(50, m[4], 3);
        Assert.Equal(60, m[5], 3);
    }

    [Fact]
    public void Center_PutsBBoxCenterAtPageCenter()
    {
        double[] m = StampPlacement.Center().ComputeMatrices(600, 800, 200, 100)[0];
        Assert.Equal(1, m[0], 3); Assert.Equal(0, m[1], 3); Assert.Equal(0, m[2], 3); Assert.Equal(1, m[3], 3);
        Assert.Equal(200, m[4], 3);
        Assert.Equal(350, m[5], 3);
    }

    [Fact]
    public void Tiled_ReturnsGridCoveringThePage()
    {
        var ms = StampPlacement.Tiled(100).ComputeMatrices(300, 200, 50, 50);
        Assert.Equal(6, ms.Count);
    }

    [Fact]
    public void Diagonal_IsCenteredAndRotated()
    {
        double[] m = StampPlacement.Diagonal().ComputeMatrices(600, 800, 200, 50)[0];
        Assert.True(System.Math.Abs(m[1]) > 0.01 && System.Math.Abs(m[2]) > 0.01);
    }
}
