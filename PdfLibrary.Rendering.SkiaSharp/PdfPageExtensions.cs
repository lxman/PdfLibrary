using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Rendering.SkiaSharp;

/// <summary>
/// Extension methods for rendering PDF pages using SkiaSharp.
/// </summary>
public static class PdfPageExtensions
{
    /// <summary>
    /// Creates a fluent builder for rendering this page using SkiaSharp.
    /// </summary>
    /// <param name="page">The PDF page to render</param>
    /// <param name="pageNumber">Page number (1-based) for logging purposes</param>
    /// <returns>A PageRenderBuilder for configuring and executing the render</returns>
    /// <example>
    /// var bitmap = page.RenderTo()
    ///     .WithScale(2.0)
    ///     .ToImage();
    /// </example>
    public static PageRenderBuilder RenderTo(this PdfPage page, int pageNumber = 1)
    {
        return new PageRenderBuilder(page, page.Document, pageNumber);
    }

    /// <summary>
    /// Renders a page and saves it to a file.
    /// Shortcut for GetPage(index).RenderTo().WithScale(scale).ToFile(filePath)
    /// </summary>
    /// <param name="doc">The PDF document</param>
    /// <param name="pageIndex">Page index (0-based)</param>
    /// <param name="filePath">Output file path (format determined by extension)</param>
    /// <param name="scale">Scale factor (default: 1.0 = 72 DPI)</param>
    public static void SavePageAs(this PdfDocument doc, int pageIndex, string filePath, double scale = 1.0)
    {
        PdfPage? page = doc.GetPage(pageIndex);
        if (page is null)
            throw new ArgumentOutOfRangeException(nameof(pageIndex), $"Page {pageIndex} not found in document");

        page.RenderTo(pageIndex + 1) // 1-based page number for logging
            .WithScale(scale)
            .ToFile(filePath);
    }
}
