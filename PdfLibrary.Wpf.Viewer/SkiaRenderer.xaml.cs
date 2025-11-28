using System.Windows.Controls;
using PdfLibrary.Content;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Rendering;
using PdfLibrary.Rendering.SkiaSharp;
using PdfLibrary.Structure;
using Serilog;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace PdfLibrary.Wpf.Viewer;

/// <summary>
/// SkiaSharp-based rendering control for PDF content.
/// Uses embedded font glyph outlines for high-fidelity text rendering.
/// </summary>
public partial class SkiaRenderer : UserControl, IRenderTarget
{
    private SkiaSharpRenderTarget? _renderTarget;
    private SKImage? _renderedImage;
    private PdfDocument? _document;
    private int _width = 800;
    private int _height = 600;

    public int CurrentPageNumber { get; set; }

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
    /// Gets the number of child elements (for debugging compatibility)
    /// </summary>
    public int GetChildCount() => _renderedImage != null ? 1 : 0;

    // ==================== PAGE LIFECYCLE ====================

    public void BeginPage(int pageNumber, double width, double height, double scale = 1.0, double cropOffsetX = 0, double cropOffsetY = 0)
    {
        CurrentPageNumber = pageNumber;
        // Calculate output dimensions (scaled)
        _width = (int)Math.Ceiling(width * scale);
        _height = (int)Math.Ceiling(height * scale);

        // Dispose previous render target
        _renderTarget?.Dispose();
        _renderedImage?.Dispose();
        _renderedImage = null;

        // Create a new render target at the scaled size
        _renderTarget = new SkiaSharpRenderTarget(_width, _height, _document);
        // Pass the original (unscaled) dimensions, scale factor, and crop offsets
        _renderTarget.BeginPage(pageNumber, width, height, scale, cropOffsetX, cropOffsetY);

        Log.Information("BeginPage: Page {PageNumber}, Size: {Width} x {Height}, Scale: {Scale:F2}, CropOffset: ({CropX}, {CropY})",
            pageNumber, _width, _height, scale, cropOffsetX, cropOffsetY);
    }

    public void EndPage()
    {
        if (_renderTarget == null) return;

        _renderTarget.EndPage();

        // Capture the rendered image
        _renderedImage?.Dispose();
        _renderedImage = _renderTarget.GetImage();

        // Update element size and trigger redraw
        SkiaCanvas.Width = _width;
        SkiaCanvas.Height = _height;
        SkiaCanvas.InvalidateVisual();

        Log.Debug("EndPage: Page {PageNumber} complete", CurrentPageNumber);
    }

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
    /// Sets the page size for rendering
    /// </summary>
    public void SetPageSize(double width, double height)
    {
        _width = (int)Math.Ceiling(width);
        _height = (int)Math.Ceiling(height);

        SkiaCanvas.Width = _width;
        SkiaCanvas.Height = _height;

        // Set minimum size to ensure ScrollViewer works correctly
        SkiaCanvas.MinWidth = _width;
        SkiaCanvas.MinHeight = _height;

        Log.Information("SetPageSize: Canvas size set to {Width} x {Height}", _width, _height);
    }

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

    // ==================== IRenderTarget DELEGATION ====================

    public void StrokePath(IPathBuilder path, PdfGraphicsState state)
    {
        _renderTarget?.StrokePath(path, state);
    }

    public void FillPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        _renderTarget?.FillPath(path, state, evenOdd);
    }

    public void FillAndStrokePath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        _renderTarget?.FillAndStrokePath(path, state, evenOdd);
    }

    public void DrawText(string text, List<double> glyphWidths, PdfGraphicsState state, PdfFont? font, List<int>? charCodes = null)
    {
        _renderTarget?.DrawText(text, glyphWidths, state, font, charCodes);
    }

    public void DrawImage(PdfImage image, PdfGraphicsState state)
    {
        _renderTarget?.DrawImage(image, state);
    }

    public void SaveState()
    {
        _renderTarget?.SaveState();
    }

    public void RestoreState()
    {
        _renderTarget?.RestoreState();
    }

    public void SetClippingPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        _renderTarget?.SetClippingPath(path, state, evenOdd);
    }

    public void ApplyCtm(System.Numerics.Matrix3x2 ctm)
    {
        _renderTarget?.ApplyCtm(ctm);
    }

    public void OnGraphicsStateChanged(PdfGraphicsState state)
    {
        _renderTarget?.OnGraphicsStateChanged(state);
    }

    public void RenderSoftMask(string maskSubtype, Action<IRenderTarget> renderMaskContent)
    {
        _renderTarget?.RenderSoftMask(maskSubtype, renderMaskContent);
    }

    public void ClearSoftMask()
    {
        _renderTarget?.ClearSoftMask();
    }

    public (int width, int height, double scale) GetPageDimensions()
    {
        return _renderTarget?.GetPageDimensions() ?? (612, 792, 1.0);
    }

    /// <summary>
    /// Gets the underlying SkiaSharp render target for advanced operations.
    /// </summary>
    public SkiaSharpRenderTarget? GetSkiaRenderTarget() => _renderTarget;
}
