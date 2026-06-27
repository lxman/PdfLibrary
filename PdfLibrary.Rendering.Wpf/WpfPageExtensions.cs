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

    /// <summary>
    /// Wraps a rendered page <see cref="DrawingGroup"/> into a frozen <see cref="DrawingImage"/>
    /// whose bounds are exactly the page rect <c>(0,0,pixelWidth,pixelHeight)</c>, ready to host
    /// under <c>Stretch="Fill"</c>/<c>Uniform</c> without distortion.
    /// </summary>
    /// <remarks>
    /// A page's natural <see cref="Drawing.Bounds"/> is the bounding box of the drawn CONTENT, which
    /// can be smaller than the page (e.g. a form whose lower half is blank) or spill slightly past
    /// it. Hosting that directly under a stretch would scale the content box to the target — wrong
    /// size and distortion. This adds a transparent page-sized rectangle to fix the bounds and clips
    /// to the page box, so the image scales as one undistorted page. Use <paramref name="pixelWidth"/>
    /// / <paramref name="pixelHeight"/> from <see cref="PageGeometry.PixelWidth"/>/<c>PixelHeight</c>
    /// at the render scale.
    /// </remarks>
    public static DrawingImage ToPageImage(this DrawingGroup pageDrawing, int pixelWidth, int pixelHeight)
    {
        var pageRect = new System.Windows.Rect(0, 0, pixelWidth, pixelHeight);
        var host = new DrawingGroup { ClipGeometry = new RectangleGeometry(pageRect) };
        host.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(pageRect)));
        host.Children.Add(pageDrawing);
        host.Freeze();
        var img = new DrawingImage(host);
        img.Freeze();
        return img;
    }
}
