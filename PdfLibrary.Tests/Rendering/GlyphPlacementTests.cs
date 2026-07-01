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
        Assert.Equal(0, r.M21, 4);    // -M21 where M21=0 -> still 0 (both terms negated; this one is a no-op)
        Assert.Equal(-3, r.M22, 4);   // -M22
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
        Assert.Equal(1, r.M21, 4);    // -M21 = -(-1) = 1
        Assert.Equal(0, r.M22, 4);    // -M22 where M22=0 -> still 0 (both terms negated; this one is a no-op)
    }

    [Fact]
    public void YFlipCompensate_RotatedText_NegatesBothM21AndM22_PreservesRotation()
    {
        // 45deg text rotation [cos sin -sin cos]. Y-flip compensation must flip the glyph's OWN Y axis
        // (negate BOTH M21 and M22, translation untouched), which preserves the rotation. The old
        // angle-heuristic negated only M22 (horizontal branch), leaving M21 with the WRONG sign, so
        // rotated glyphs came out mirrored/skewed — the TextBasics.pdf bottom-row defect.
        const float c = 0.70710677f;                       // cos45 = sin45
        var m = new Matrix3x2(c, c, -c, c, 100, 200);
        Matrix3x2 r = GlyphPlacement.YFlipCompensate(m);
        Assert.Equal(c, r.M11, 4);
        Assert.Equal(c, r.M12, 4);
        Assert.Equal(c, r.M21, 4);     // -(-c) = +c  (buggy code left this at -c → skew)
        Assert.Equal(-c, r.M22, 4);    // -(c)
        Assert.Equal(100, r.M31, 4);   // translation must NOT change
        Assert.Equal(200, r.M32, 4);
    }

    [Fact]
    public void GlyphToUser_HorizontalScaling_DoesNotDoubleScaleAdvance()
    {
        // Tz (horizontal scaling) is ALREADY baked into the advance width upstream (PdfRenderer:
        // advance *= Th). GlyphToUser must place currentX OUTSIDE the tHs glyph-shape scale — otherwise
        // Th is applied twice and glyphs pack to Th^2, overlapping at Tz<100% (TextLayout.pdf 50%/75%,
        // and the 80% "Condensed text example" that looked mis-kerned).
        var state = new PdfGraphicsState();
        state.SetTextMatrix(1, 0, 0, 1, 0, 0);     // identity orientation at origin
        state.TextRise = 0;
        Matrix3x2 r = GlyphPlacement.GlyphToUser(state, currentX: 10, tHs: 0.5f);

        // The glyph ORIGIN sits at the advance (currentX), which must NOT be re-scaled by tHs.
        Vector2 origin = Vector2.Transform(Vector2.Zero, r);
        Assert.Equal(10, origin.X, 3);             // bug placed it at currentX*tHs = 5 -> overlap
        Assert.Equal(0, origin.Y, 3);

        // The glyph SHAPE is still horizontally scaled by tHs: a unit-X glyph point lands 0.5 past origin.
        Vector2 unitX = Vector2.Transform(new Vector2(1, 0), r);
        Assert.Equal(10.5, unitX.X, 3);            // 10 (position) + 1*0.5 (shape scaled by tHs)
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
