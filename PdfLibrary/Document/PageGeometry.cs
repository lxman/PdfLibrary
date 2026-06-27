using System.Numerics;
using PdfLibrary.Builder;

namespace PdfLibrary.Document;

/// <summary>An axis-aligned rectangle in rendered-image pixel space: origin top-left, Y increasing
/// downward. Ready for screen/Canvas placement (X,Y = top-left corner).</summary>
public readonly struct ImageRect(double x, double y, double width, double height)
{
    /// <summary>Left edge, pixels from the image's left.</summary>
    public double X { get; } = x;
    /// <summary>Top edge, pixels from the image's top.</summary>
    public double Y { get; } = y;
    /// <summary>Width of the rectangle in pixels.</summary>
    public double Width { get; } = width;
    /// <summary>Height of the rectangle in pixels.</summary>
    public double Height { get; } = height;
}

/// <summary>
/// Maps a PDF page between user space (Y-up, PDF points) and rendered-image pixels
/// (Y-down, top-left origin) at a chosen scale — the same transform the renderers apply.
/// Use it to place UI (e.g. form-field controls) over a rendered page, and to hit-test clicks.
/// </summary>
public readonly struct PageGeometry
{
    /// <summary>PDF user space → image pixels.</summary>
    public Matrix3x2 PdfToImage { get; }
    /// <summary>Image pixels → PDF user space (inverse of <see cref="PdfToImage"/>).</summary>
    public Matrix3x2 ImageToPdf { get; }
    /// <summary>Rendered image width in pixels at this scale.</summary>
    public int PixelWidth { get; }
    /// <summary>Rendered image height in pixels at this scale.</summary>
    public int PixelHeight { get; }

    internal PageGeometry(Matrix3x2 pdfToImage, int pixelWidth, int pixelHeight)
    {
        PdfToImage = pdfToImage;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        ImageToPdf = Matrix3x2.Invert(pdfToImage, out Matrix3x2 inv) ? inv : Matrix3x2.Identity;
    }

    /// <summary>Maps a rect in PDF user space to an axis-aligned pixel rect in rendered-image space
    /// (origin top-left, Y increasing downward). Use <see cref="ImageRect.X"/>/<see cref="ImageRect.Y"/>
    /// as the top-left corner when placing a UI control over the rendered page.</summary>
    public ImageRect MapRectToImage(PdfRect pdfRect)
    {
        Vector2 a = Vector2.Transform(new Vector2((float)pdfRect.Left, (float)pdfRect.Bottom), PdfToImage);
        Vector2 b = Vector2.Transform(new Vector2((float)pdfRect.Right, (float)pdfRect.Top), PdfToImage);
        double x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
        return new ImageRect(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }
}
