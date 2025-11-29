using System.Windows.Controls;
using PdfLibrary.Rendering.SkiaSharp;
using PdfLibrary.Structure;
using Serilog;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace PdfLibrary.Wpf.Viewer;

/// <summary>
/// SkiaSharp-based rendering control for PDF content.
/// Wraps a SkiaSharpRenderTarget and displays the rendered output.
/// </summary>
public partial class SkiaRenderer : UserControl
{
    private SkiaSharpRenderTarget? _renderTarget;
    private SKImage? _renderedImage;
    private PdfDocument? _document;
    private int _width = 800;
    private int _height = 600;

    public int CurrentPageNumber { get; private set; }

    public SkiaRenderer()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Sets the PDF document for resolving indirect references
    /// </summary>
    public void SetDocument(PdfDocument? document)
    {
        _document = document;
    }

    /// <summary>
    /// Gets the underlying SkiaSharp render target.
    /// Creates a new render target if needed based on the page dimensions.
    /// </summary>
    public SkiaSharpRenderTarget GetOrCreateRenderTarget(double width, double height, double scale)
    {
        // Calculate output dimensions (scaled)
        _width = (int)Math.Ceiling(width * scale);
        _height = (int)Math.Ceiling(height * scale);

        // Dispose previous render target
        _renderTarget?.Dispose();
        _renderedImage?.Dispose();
        _renderedImage = null;

        // Create a new render target at the scaled size
        _renderTarget = new SkiaSharpRenderTarget(_width, _height, _document);

        return _renderTarget;
    }

    /// <summary>
    /// Called after rendering to capture the image and update display
    /// </summary>
    public void FinalizeRendering(int pageNumber)
    {
        CurrentPageNumber = pageNumber;

        if (_renderTarget == null) return;

        // Capture the rendered image
        _renderedImage?.Dispose();
        _renderedImage = _renderTarget.GetImage();

        // Update element size and trigger redraw
        SkiaCanvas.Width = _width;
        SkiaCanvas.Height = _height;
        SkiaCanvas.InvalidateVisual();

        Log.Debug("FinalizeRendering: Page {PageNumber} complete", CurrentPageNumber);
    }

    /// <summary>
    /// Clears the rendered content
    /// </summary>
    public void Clear()
    {
        _renderTarget?.Clear();
        _renderedImage?.Dispose();
        _renderedImage = null;
        CurrentPageNumber = 0;

        SkiaCanvas.InvalidateVisual();

        Log.Debug("Clear: Canvas cleared");
    }

    /// <summary>
    /// Gets the number of child elements (for debugging compatibility)
    /// </summary>
    public int GetChildCount() => _renderedImage != null ? 1 : 0;

    // ==================== SKIA PAINT EVENT ====================

    private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        SKCanvas? canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.White);

        if (_renderedImage != null)
        {
            // Draw the pre-rendered image
            canvas.DrawImage(_renderedImage, 0, 0);
        }
    }
}
