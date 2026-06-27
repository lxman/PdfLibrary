using System.Numerics;
using PdfLibrary.Builder;

namespace PdfLibrary.Document;

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

    /// <summary>Maps a rect in PDF user space to a normalized image-pixel rect.</summary>
    public PdfRect MapRectToImage(PdfRect pdfRect)
    {
        Vector2 a = Vector2.Transform(new Vector2((float)pdfRect.Left, (float)pdfRect.Bottom), PdfToImage);
        Vector2 b = Vector2.Transform(new Vector2((float)pdfRect.Right, (float)pdfRect.Top), PdfToImage);
        return new PdfRect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }
}
