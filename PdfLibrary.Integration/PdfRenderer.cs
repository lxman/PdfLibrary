using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;
using SkiaSharp;

namespace PdfLibrary.Integration;

/// <summary>
/// Renders PDF files to images using PdfLibrary
/// </summary>
public static class PdfImageRenderer
{
    /// <summary>
    /// Renders a PDF page to a PNG file
    /// </summary>
    /// <param name="pdfPath">Path to the PDF file</param>
    /// <param name="outputPath">Path to save the output PNG</param>
    /// <param name="scale">Scale factor (1.0 = 72 DPI)</param>
    /// <param name="pageNumber">1-based page number</param>
    public static void RenderToImage(string pdfPath, string outputPath, double scale = 1.0, int pageNumber = 1)
    {
        using var stream = File.OpenRead(pdfPath);
        var document = PdfDocument.Load(stream);

        int pageIndex = pageNumber - 1;
        PdfPage page = document.GetPage(pageIndex)
            ?? throw new InvalidOperationException($"Page {pageNumber} not found in {pdfPath}");

        var cropBox = page.GetCropBox();
        int width = (int)(cropBox.Width * scale);
        int height = (int)(cropBox.Height * scale);

        using var renderTarget = new SkiaSharpRenderTarget(width, height, document);
        var resources = page.GetResources();
        var optionalContentManager = new OptionalContentManager(document);
        var renderer = new PdfLibrary.Rendering.PdfRenderer(renderTarget, resources, optionalContentManager, document);

        renderer.RenderPage(page, pageNumber, scale);
        renderTarget.SaveToFile(outputPath, SKEncodedImageFormat.Png, 100);
    }
}
