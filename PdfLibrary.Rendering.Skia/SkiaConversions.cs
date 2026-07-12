using System.Numerics;
using PdfLibrary.Rendering;
using SkiaSharp;

namespace PdfLibrary.Rendering.Skia;

/// <summary>Affine + color + path conversions from PdfLibrary/Numerics types to SkiaSharp.</summary>
public static class SkiaConversions
{
    /// <summary>Matrix3x2 uses row-vector action (x' = x*M11 + y*M21 + M31). SKMatrix.MapPoint uses
    /// x' = ScaleX*x + SkewX*y + TransX, so map ScaleX=M11, SkewX=M21, TransX=M31, SkewY=M12,
    /// ScaleY=M22, TransY=M32 to preserve the exact algebraic action.</summary>
    public static SKMatrix ToSkMatrix(Matrix3x2 m) =>
        new(m.M11, m.M21, m.M31,
            m.M12, m.M22, m.M32,
            0, 0, 1);

    public static SKColor ToSkColor(IReadOnlyList<double> resolved, string colorSpace, double alpha)
    {
        (byte r, byte g, byte b) = PdfColorToRgb.ToRgb(resolved, colorSpace);
        return new SKColor(r, g, b, (byte)Math.Clamp(alpha * 255.0, 0, 255));
    }

    public static SKPath ToSkPath(IReadOnlyList<PathSegment> segments, bool evenOdd)
    {
        var path = new SKPath { FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding };
        var open = false;
        foreach (PathSegment seg in segments)
        {
            switch (seg)
            {
                case MoveToSegment m:  path.MoveTo((float)m.X, (float)m.Y); open = true; break;
                case LineToSegment l:  if (open) path.LineTo((float)l.X, (float)l.Y); break;
                case CurveToSegment c: if (open) path.CubicTo((float)c.X1, (float)c.Y1, (float)c.X2, (float)c.Y2, (float)c.X3, (float)c.Y3); break;
                case ClosePathSegment: if (open) path.Close(); break;
            }
        }
        return path;
    }
}
