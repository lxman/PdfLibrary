using PdfLibrary.Document;
using PdfLibrary.Rendering.SkiaSharp;
using PdfLibrary.Structure;

namespace PdfLibrary.Integration;

/// <summary>
/// Renders PDF files to images using PdfLibrary
/// </summary>
public static class PdfImageRenderer
{
    /// <summary>
    /// Gets the number of pages in a PDF file
    /// </summary>
    /// <param name="pdfPath">Path to the PDF file</param>
    /// <returns>Number of pages</returns>
    public static int GetPageCount(string pdfPath)
    {
        using FileStream stream = File.OpenRead(pdfPath);
        PdfDocument document = PdfDocument.Load(stream);
        return document.PageCount;
    }

    /// <summary>
    /// Renders a PDF page to a PNG file
    /// </summary>
    /// <param name="pdfPath">Path to the PDF file</param>
    /// <param name="outputPath">Path to save the output PNG</param>
    /// <param name="scale">Scale factor (1.0 = 72 DPI)</param>
    /// <param name="pageNumber">1-based page number</param>
    public static void RenderToImage(string pdfPath, string outputPath, double scale = 1.0, int pageNumber = 1)
    {
        using FileStream stream = File.OpenRead(pdfPath);
        PdfDocument document = PdfDocument.Load(stream);

        int pageIndex = pageNumber - 1;
        PdfPage page = document.GetPage(pageIndex)
            ?? throw new InvalidOperationException($"Page {pageNumber} not found in {pdfPath}");

        PdfRectangle cropBox = page.GetCropBox();
        var width = (int)(cropBox.Width * scale);
        var height = (int)(cropBox.Height * scale);

        // Use the public PdfPage.Render() API
        using var renderTarget = new SkiaSharpRenderTarget(width, height, document);
        page.Render(renderTarget, pageNumber, scale);
        renderTarget.SaveToFile(outputPath);
    }
}
