using System.Numerics;
using PdfLibrary.Content;

namespace PdfLibrary.Rendering;

/// <summary>
/// Builds the glyph-space → user-space transform for a single glyph (SkiaSharp-free port of
/// TextRenderer.RenderGlyph's matrix build + CreateYFlipCompensatedMatrix). The page canvas
/// applies the CTM separately, so this matrix carries only the text-space positioning.
/// </summary>
internal static class GlyphPlacement
{
    public static Matrix3x2 GlyphToUser(PdfGraphicsState state, double currentX, float tHs)
    {
        var tRise = (float)state.TextRise;

        // textState: horizontal scaling on X, text rise on the translation row.
        var textStateMatrix = new Matrix3x2(
            tHs, 0,
            0, 1,
            0, tRise);

        // Order matters: apply the text-state (tHs glyph-shape scale + rise) FIRST, THEN translate by the
        // advance, THEN the text matrix. currentX must land OUTSIDE the tHs scale — the advance already
        // carries horizontal scaling (PdfRenderer bakes advance *= Th). Placing the translation inside the
        // tHs scale (translation * textState) would apply Th twice, packing glyphs to Th^2 and overlapping
        // them at Tz<100% (TextLayout.pdf 50%/75%, and the 80% condensed run). At Tz=100% (tHs=1) both
        // orders are identical, so ordinary and rotated text are unaffected.
        Matrix3x2 translationMatrix = Matrix3x2.CreateTranslation((float)currentX, 0);
        Matrix3x2 fullGlyphMatrix = textStateMatrix * translationMatrix * state.TextMatrix;

        return YFlipCompensate(fullGlyphMatrix);
    }

    /// <summary>
    /// Compensates for the canvas's global Y-flip by flipping the glyph's OWN Y axis (a pre-transform
    /// <c>diag(1,-1) · m</c>), i.e. negating both M21 and M22 while leaving the translation untouched.
    /// This is angle-independent, so it preserves the text rotation for any orientation.
    ///
    /// The previous angle-heuristic negated only M22 (horizontal) or only M21 (vertical). That is correct
    /// ONLY at exactly 0deg / 90deg (where the other term is zero); at every intermediate angle it left one
    /// off-diagonal term with the wrong sign, mirroring/skewing rotated glyphs — and at exactly 45deg the
    /// resulting linear part became singular (det = -cos 2θ = 0), collapsing the glyph to nothing.
    /// (TextBasics.pdf bottom row: 15/30/60deg skewed, 45deg missing.)
    /// </summary>
    public static Matrix3x2 YFlipCompensate(Matrix3x2 m) =>
        new(m.M11, m.M12, -m.M21, -m.M22, m.M31, m.M32);
}
