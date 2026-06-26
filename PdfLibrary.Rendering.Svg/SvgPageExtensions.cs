using PdfLibrary.Document;

namespace PdfLibrary.Rendering.Svg;

public static class SvgPageExtensions
{
    /// <summary>Render a page to a standalone SVG document string.</summary>
    public static string RenderToSvg(this PdfPage page, double scale = 1.0)
    {
        var target = new SvgRenderTarget();
        page.Render(target, pageNumber: 1, scale: scale);   // BeginPage is called by PdfRenderer.RenderPage
        return target.GetSvg();
    }
}
