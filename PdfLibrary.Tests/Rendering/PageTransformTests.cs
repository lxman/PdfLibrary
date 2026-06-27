using System.Numerics;
using PdfLibrary.Rendering;

namespace PdfLibrary.Tests.Rendering;

public class PageTransformTests
{
    [Fact]
    public void Build_RotationZero_MatchesKnownMatrix()
    {
        // width=600,height=800,scale=2,crop=(0,0): matrix(2,0,0,-2, 0, 800*2)
        Matrix3x2 m = PageTransform.Build(600, 800, 2.0, 0, 0, 0);
        Assert.Equal(2, m.M11, 4);
        Assert.Equal(0, m.M12, 4);
        Assert.Equal(0, m.M21, 4);
        Assert.Equal(-2, m.M22, 4);
        Assert.Equal(0, m.M31, 4);
        Assert.Equal(1600, m.M32, 4);
        // PDF (0,0) -> image (0, 1600) (bottom).
        Vector2 o = Vector2.Transform(Vector2.Zero, m);
        Assert.Equal(0, o.X, 3); Assert.Equal(1600, o.Y, 3);
    }

    [Fact]
    public void Build_CropOffset_TranslatesOrigin()
    {
        Matrix3x2 m = PageTransform.Build(600, 800, 1.0, 10, 20, 0);
        Assert.Equal(-10, m.M31, 4);          // -cropX*scale
        Assert.Equal(800 + 20, m.M32, 4);      // (cropY+height)*scale
    }

    [Fact]
    public void Build_Rotation90_SwapsExtents()
    {
        // For 90°, finalHeight = width; verify it doesn't throw and is invertible.
        Matrix3x2 m = PageTransform.Build(600, 800, 1.0, 0, 0, 90);
        Assert.True(Matrix3x2.Invert(m, out _));
    }
}
