namespace PdfLibrary.Rendering;

/// <summary>
/// Fluent interface for rendering PDF pages.
/// Implemented by platform-specific rendering packages.
/// </summary>
/// <typeparam name="TImage">The platform-specific image type returned by ToImage()</typeparam>
public interface IRenderBuilder<out TImage> where TImage : IDisposable
{
    /// <summary>
    /// Sets the scale factor for rendering (default: 1.0 = 72 DPI).
    /// </summary>
    /// <param name="scale">Scale factor (e.g., 2.0 for 144 DPI)</param>
    IRenderBuilder<TImage> WithScale(double scale);

    /// <summary>
    /// Sets the DPI for rendering (converts to scale factor).
    /// </summary>
    /// <param name="dpi">Target DPI (72 = native PDF resolution)</param>
    IRenderBuilder<TImage> WithDpi(double dpi);

    /// <summary>
    /// Makes the background transparent (for PNG output with alpha).
    /// By default, pages render with a white background.
    /// </summary>
    IRenderBuilder<TImage> WithTransparentBackground();

    /// <summary>
    /// Renders the page and returns the result as a platform-specific image.
    /// </summary>
    /// <returns>The rendered image (caller must dispose)</returns>
    TImage ToImage();

    /// <summary>
    /// Renders the page and saves it to a file.
    /// Format is determined by file extension (.png, .jpg, .webp, etc.)
    /// </summary>
    /// <param name="filePath">Output file path</param>
    /// <param name="quality">Quality for lossy formats (0-100, default: 100)</param>
    void ToFile(string filePath, int quality = 100);

    /// <summary>
    /// Renders the page to a stream in the specified format.
    /// </summary>
    /// <param name="stream">Output stream</param>
    /// <param name="format">Image format</param>
    /// <param name="quality">Quality for lossy formats (0-100, default: 100)</param>
    void ToStream(Stream stream, ImageFormat format = ImageFormat.Png, int quality = 100);

    /// <summary>
    /// Renders the page and returns the encoded bytes.
    /// </summary>
    /// <param name="format">Image format</param>
    /// <param name="quality">Quality for lossy formats (0-100, default: 100)</param>
    /// <returns>Encoded image bytes</returns>
    byte[] ToBytes(ImageFormat format = ImageFormat.Png, int quality = 100);
}
