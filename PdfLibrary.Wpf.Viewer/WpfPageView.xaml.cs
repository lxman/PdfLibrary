using System.Windows.Controls;
using System.Windows.Media;
using PdfLibrary.Rendering.Wpf;

namespace PdfLibrary.Wpf.Viewer;

/// <summary>
/// WPF vector page host: displays a <see cref="DrawingGroup"/> produced by
/// <c>WpfPageExtensions.RenderToDrawing</c> and exposes a transparent overlay
/// <see cref="Canvas"/> for form-field controls (Task 3).
/// </summary>
public partial class WpfPageView : UserControl
{
    public WpfPageView() => InitializeComponent();

    /// <summary>The overlay Canvas for placing form-field controls.</summary>
    public Canvas Overlay => OverlayCanvas;

    /// <summary>
    /// Display a rendered page.
    /// <paramref name="pixelWidth"/>/<paramref name="pixelHeight"/> are the DrawingGroup's
    /// pixel dimensions; <paramref name="dpiScale"/> converts pixels to DIUs so the control
    /// sizes correctly on high-DPI displays.
    /// </summary>
    public void ShowPage(DrawingGroup pageDrawing, int pixelWidth, int pixelHeight, double dpiScale)
    {
        // The page DrawingGroup's natural Bounds is the bounding box of the drawn CONTENT, which can
        // be smaller than the page (e.g. a form whose lower half is blank). Under Stretch="Fill"
        // that content box would stretch to the page size — wrong size, distortion, misaligned
        // overlay. ToPageImage wraps it to exact page-rect bounds so the stretch is a clean 1:1.
        PageImage.Source = pageDrawing.ToPageImage(pixelWidth, pixelHeight);

        double diuW = pixelWidth / dpiScale;
        double diuH = pixelHeight / dpiScale;

        Root.Width = diuW;
        Root.Height = diuH;
        PageImage.Width = diuW;
        PageImage.Height = diuH;
        OverlayCanvas.Width = diuW;
        OverlayCanvas.Height = diuH;

        // Overlay is rebuilt after every render (Task 3 fills this in).
        OverlayCanvas.Children.Clear();
    }
}
