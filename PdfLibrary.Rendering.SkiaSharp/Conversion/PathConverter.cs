using PdfLibrary.Rendering;
using SkiaSharp;

namespace PdfLibrary.Rendering.SkiaSharp.Conversion;

/// <summary>
/// Converts PDF path operations to SkiaSharp SKPath.
/// </summary>
internal static class PathConverter
{
    /// <summary>
    /// Convert IPathBuilder to SkiaSharp SKPath.
    /// </summary>
    /// <param name="pathBuilder">PDF path builder with MoveTo, LineTo, CurveTo, Close segments</param>
    /// <returns>SkiaSharp path ready for rendering</returns>
    public static SKPath ConvertToSkPath(IPathBuilder pathBuilder)
    {
        var skPath = new SKPath();

        if (pathBuilder is not PathBuilder builder)
            return skPath;

        foreach (PathSegment segment in builder.Segments)
        {
            switch (segment)
            {
                case MoveToSegment moveTo:
                    skPath.MoveTo((float)moveTo.X, (float)moveTo.Y);
                    break;

                case LineToSegment lineTo:
                    skPath.LineTo((float)lineTo.X, (float)lineTo.Y);
                    break;

                case CurveToSegment curveTo:
                    // PDF uses cubic Bézier curves (4 control points)
                    skPath.CubicTo(
                        (float)curveTo.X1, (float)curveTo.Y1,
                        (float)curveTo.X2, (float)curveTo.Y2,
                        (float)curveTo.X3, (float)curveTo.Y3
                    );
                    break;

                case ClosePathSegment:
                    skPath.Close();
                    break;
            }
        }

        return skPath;
    }
}
