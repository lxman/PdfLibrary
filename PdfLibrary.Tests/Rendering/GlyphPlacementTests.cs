using System.Numerics;
using PdfLibrary.Content;
using PdfLibrary.Rendering;

namespace PdfLibrary.Tests.Rendering;

public class GlyphPlacementTests
{
    [Fact]
    public void YFlipCompensate_Horizontal_NegatesM22()
    {
        var m = new Matrix3x2(2, 0, 0, 3, 5, 7); // rotation 0 -> horizontal
        Matrix3x2 r = GlyphPlacement.YFlipCompensate(m);
        Assert.Equal(2, r.M11, 4);
        Assert.Equal(0, r.M12, 4);
        Assert.Equal(0, r.M21, 4);
        Assert.Equal(-3, r.M22, 4);   // horizontal -> negate M22
        Assert.Equal(5, r.M31, 4);
        Assert.Equal(7, r.M32, 4);
    }

    [Fact]
    public void YFlipCompensate_Vertical_NegatesM21()
    {
        // 90deg rotation: M11=0, M12=1, M21=-1, M22=0
        var m = new Matrix3x2(0, 1, -1, 0, 0, 0);
        Matrix3x2 r = GlyphPlacement.YFlipCompensate(m);
        Assert.Equal(0, r.M11, 4);
        Assert.Equal(1, r.M12, 4);
        Assert.Equal(1, r.M21, 4);    // vertical -> negate M21 (was -1)
        Assert.Equal(0, r.M22, 4);
    }

    [Fact]
    public void GlyphToUser_AppliesTextStateTextMatrixAndTranslation()
    {
        var state = new PdfGraphicsState();
        state.SetTextMatrix(1, 0, 0, 1, 100, 200); // identity orientation, origin (100,200)
        state.TextRise = 0;
        // tHs = 1 (100% horizontal scaling)
        Matrix3x2 r = GlyphPlacement.GlyphToUser(state, currentX: 10, tHs: 1f);
        // glyph origin (0,0) -> translation(10,0) then TextMatrix -> (110, 200); Y not flipped at origin
        Vector2 origin = Vector2.Transform(Vector2.Zero, r);
        Assert.Equal(110, origin.X, 3);
        Assert.Equal(200, origin.Y, 3);
    }
}
