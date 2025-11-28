using SkiaSharp;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Rendering;

/// <summary>
/// Fluent builder for rendering PDF pages with various options.
/// </summary>
public class PageRenderBuilder
{
    private readonly PdfPage _page;
    private readonly PdfDocument? _document;
    private double _scale = 1.0;
    private bool _transparentBackground;
    private int _pageNumber = 1;

    internal PageRenderBuilder(PdfPage page, PdfDocument? document = null, int pageNumber = 1)
    {
        _page = page;
        _document = document;
        _pageNumber = pageNumber;
    }

    /// <summary>
    /// Sets the scale factor for rendering (default: 1.0 = 72 DPI).
    /// </summary>
    /// <param name="scale">Scale factor (e.g., 2.0 for 144 DPI)</param>
    public PageRenderBuilder WithScale(double scale)
    {
        _scale = scale;
        return this;
    }

    /// <summary>
    /// Sets the DPI for rendering (converts to scale factor).
    /// </summary>
    /// <param name="dpi">Target DPI (72 = native PDF resolution)</param>
    public PageRenderBuilder WithDpi(double dpi)
    {
        _scale = dpi / 72.0;
        return this;
    }

    /// <summary>
    /// Makes the background transparent (for PNG output with alpha).
    /// By default, pages render with a white background.
    /// </summary>
    public PageRenderBuilder WithTransparentBackground()
    {
        _transparentBackground = true;
        return this;
    }

    /// <summary>
    /// Renders the page and returns the result as an SKImage.
    /// </summary>
    /// <returns>The rendered image (caller must dispose)</returns>
    public SKImage ToImage()
    {
        var width = (int)Math.Ceiling(_page.Width * _scale);
        var height = (int)Math.Ceiling(_page.Height * _scale);

        using var target = new SkiaSharpRenderTarget(width, height, _document, _transparentBackground);

        var renderer = new PdfRenderer(target, null, null, _document);
        renderer.RenderPage(_page, _pageNumber, _scale);

        return target.GetImage();
    }

    /// <summary>
    /// Renders the page and saves it to a file.
    /// Format is determined by file extension (.png, .jpg, .webp, etc.)
    /// </summary>
    /// <param name="filePath">Output file path</param>
    /// <param name="quality">Quality for lossy formats (0-100, default: 100)</param>
    public void ToFile(string filePath, int quality = 100)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        SKEncodedImageFormat format = extension switch
        {
            ".png" => SKEncodedImageFormat.Png,
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            ".webp" => SKEncodedImageFormat.Webp,
            ".gif" => SKEncodedImageFormat.Gif,
            ".bmp" => SKEncodedImageFormat.Bmp,
            _ => SKEncodedImageFormat.Png
        };

        using SKImage image = ToImage();
        using SKData? data = image.Encode(format, quality);
        using FileStream stream = File.OpenWrite(filePath);
        data.SaveTo(stream);
    }

    /// <summary>
    /// Renders the page to a stream in the specified format.
    /// </summary>
    /// <param name="stream">Output stream</param>
    /// <param name="format">Image format</param>
    /// <param name="quality">Quality for lossy formats (0-100, default: 100)</param>
    public void ToStream(Stream stream, SKEncodedImageFormat format = SKEncodedImageFormat.Png, int quality = 100)
    {
        using SKImage image = ToImage();
        using SKData? data = image.Encode(format, quality);
        data.SaveTo(stream);
    }

    /// <summary>
    /// Renders the page and returns the encoded bytes.
    /// </summary>
    /// <param name="format">Image format</param>
    /// <param name="quality">Quality for lossy formats (0-100, default: 100)</param>
    /// <returns>Encoded image bytes</returns>
    public byte[] ToBytes(SKEncodedImageFormat format = SKEncodedImageFormat.Png, int quality = 100)
    {
        using SKImage image = ToImage();
        using SKData? data = image.Encode(format, quality);
        return data.ToArray();
    }
}
