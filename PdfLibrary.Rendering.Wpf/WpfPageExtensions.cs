using System.Windows.Media;
using PdfLibrary.Document;

namespace PdfLibrary.Rendering.Wpf;

public static class WpfPageExtensions
{
    /// <summary>Render a page to a WPF <see cref="DrawingGroup"/> (retained vector).</summary>
    /// <remarks>
    /// Mirrors <c>SvgPageExtensions.RenderToSvg</c>: constructs a <see cref="WpfRenderTarget"/>,
    /// drives the same <see cref="PdfPage.Render"/> pipeline that the SVG extension uses
    /// (<c>BeginPage</c> is called internally by <c>PdfRenderer.RenderPage</c>), then returns
    /// <see cref="WpfRenderTarget.Drawing"/>.  Must be called on an STA thread.
    /// </remarks>
    public static DrawingGroup RenderToDrawing(this PdfPage page, double scale = 1.0)
    {
        var target = new WpfRenderTarget(page.Document);
        page.Render(target, pageNumber: 1, scale: scale);
        return target.Drawing;
    }
}
