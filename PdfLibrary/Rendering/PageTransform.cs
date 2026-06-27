using System.Numerics;

namespace PdfLibrary.Rendering;

/// <summary>
/// Builds the page initial transform: PDF user space (Y-up, points) → rendered-image pixels
/// (Y-down, top-left origin) at a scale, accounting for crop-box offset and page rotation.
/// Single source of truth shared by the render targets and <see cref="PdfLibrary.Document.PdfPage.GetGeometry"/>.
/// </summary>
public static class PageTransform
{
    /// <param name="width">CropBox width (PDF points).</param>
    /// <param name="height">CropBox height (PDF points).</param>
    /// <param name="scale">Render scale (1.0 = 72 DPI).</param>
    /// <param name="cropX">CropBox lower-left X (offset from MediaBox origin).</param>
    /// <param name="cropY">CropBox lower-left Y.</param>
    /// <param name="rotation">Page rotation, normalized 0/90/180/270.</param>
    public static Matrix3x2 Build(double width, double height, double scale,
        double cropX, double cropY, int rotation)
    {
        double finalHeight = rotation is 90 or 270 ? width : height;
        (float tx, float ty) = rotation switch
        {
            90 => (0f, (float)width),
            180 => ((float)width, (float)height),
            270 => ((float)height, 0f),
            _ => (0f, 0f)
        };
        var rad = (float)(-rotation * Math.PI / 180.0);
        return Matrix3x2.CreateTranslation((float)-cropX, (float)-cropY)
             * Matrix3x2.CreateRotation(rad)
             * Matrix3x2.CreateTranslation(tx, ty)
             * Matrix3x2.CreateScale((float)scale, (float)-scale)
             * Matrix3x2.CreateTranslation(0f, (float)(finalHeight * scale));
    }
}
