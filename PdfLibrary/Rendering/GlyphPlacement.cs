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

        Matrix3x2 glyphMatrix = textStateMatrix * state.TextMatrix;
        Matrix3x2 translationMatrix = Matrix3x2.CreateTranslation((float)currentX, 0);
        Matrix3x2 fullGlyphMatrix = translationMatrix * glyphMatrix;

        return YFlipCompensate(fullGlyphMatrix);
    }

    /// <summary>
    /// Compensates for the canvas's global Y-flip. Horizontal text negates M22; vertical text
    /// (|rotation| near 90deg) negates M21 instead. (Port of CreateYFlipCompensatedMatrix.)
    /// </summary>
    public static Matrix3x2 YFlipCompensate(Matrix3x2 m)
    {
        double rotationDeg = System.Math.Atan2(m.M12, m.M11) * (180.0 / System.Math.PI);
        while (rotationDeg > 180) rotationDeg -= 360;
        while (rotationDeg < -180) rotationDeg += 360;

        bool isVertical = System.Math.Abs(System.Math.Abs(rotationDeg) - 90) < 45;

        return isVertical
            ? new Matrix3x2(m.M11, m.M12, -m.M21, m.M22, m.M31, m.M32)
            : new Matrix3x2(m.M11, m.M12, m.M21, -m.M22, m.M31, m.M32);
    }
}
