using System.Numerics;
using System.Windows;
using System.Windows.Media;
using PdfLibrary.Content;
using PdfLibrary.Document;
using PdfLibrary.Rendering;

namespace PdfLibrary.Rendering.Wpf;

/// <summary>
/// A SkiaSharp-free <see cref="IRenderTarget"/> that renders a PDF page into a WPF
/// <see cref="DrawingGroup"/> (retained vector — crisp at any zoom).  The page initial
/// transform (from <see cref="PageTransform"/>) is set directly on the root DrawingGroup so
/// callers can read <see cref="Drawing"/>.Transform without digging into child groups.
/// Paths arrive CTM-baked in PDF user space; sub-groups are pushed for clip and save-state.
/// </summary>
/// <remarks>
/// All public members that open a WPF <see cref="DrawingContext"/> must be called on an STA
/// thread.  Tests use the <c>Sta.Run</c> helper in PdfLibrary.Rendering.Wpf.Tests.
/// </remarks>
public sealed class WpfRenderTarget : IRenderTarget
{
    private DrawingGroup _rootGroup = new();
    private DrawingVisual _visual = new();
    private DrawingContext? _dc;

    // Tracks outstanding PushXxx() calls so EndPage can balance them.
    private int _pushDepth;
    private readonly Stack<int> _saveStack = new();

    private (int w, int h, double scale) _dims;

    /// <summary>The finished drawing tree after <see cref="EndPage"/>.</summary>
    public DrawingGroup Drawing => _rootGroup;

    /// <summary>
    /// A <see cref="DrawingVisual"/> wrapping <see cref="Drawing"/> — suitable for
    /// <see cref="System.Windows.Media.Imaging.RenderTargetBitmap"/>.  Valid after
    /// <see cref="EndPage"/>.
    /// </summary>
    public DrawingVisual Visual => _visual;

    public int CurrentPageNumber { get; private set; }

    // ==================== PAGE LIFECYCLE ====================

    public void BeginPage(int pageNumber, double width, double height, double scale = 1.0,
        double cropOffsetX = 0, double cropOffsetY = 0, int rotation = 0)
    {
        CurrentPageNumber = pageNumber;
        _saveStack.Clear();
        _pushDepth = 0;

        double pw = (rotation is 90 or 270 ? height : width) * scale;
        double ph = (rotation is 90 or 270 ? width : height) * scale;
        _dims = ((int)Math.Ceiling(pw), (int)Math.Ceiling(ph), scale);

        // Build the page initial transform (PDF user space → rendered pixels).
        Matrix3x2 m = PageTransform.Build(width, height, scale, cropOffsetX, cropOffsetY, rotation);

        // Set the transform directly on the root DrawingGroup so Drawing.Transform
        // is immediately readable (PushTransform would only set it on a child group).
        _rootGroup = new DrawingGroup
        {
            Transform = new MatrixTransform(m.M11, m.M12, m.M21, m.M22, m.M31, m.M32)
        };

        _dc = _rootGroup.Open();
    }

    public void EndPage()
    {
        if (_dc is null) return;

        // Balance any outstanding Push calls (from SaveState / SetClippingPath).
        while (_pushDepth-- > 0) _dc.Pop();

        _dc.Close();
        _dc = null;

        // Build the visual so callers can pass it to RenderTargetBitmap.
        _visual = new DrawingVisual();
        using DrawingContext vdc = _visual.RenderOpen();
        vdc.DrawDrawing(_rootGroup);
    }

    public void Clear()
    {
        _rootGroup = new DrawingGroup();
        _visual = new DrawingVisual();
        _saveStack.Clear();
        _pushDepth = 0;
        CurrentPageNumber = 0;
    }

    public (int width, int height, double scale) GetPageDimensions() => _dims;

    // ==================== STATE MANAGEMENT (stubs — Task D2b-2) ====================

    public void SaveState() => _saveStack.Push(_pushDepth);

    public void RestoreState()
    {
        if (_saveStack.Count == 0 || _dc is null) return;
        int target = _saveStack.Pop();
        while (_pushDepth > target) { _dc.Pop(); _pushDepth--; }
    }

    public void ApplyCtm(Matrix3x2 ctm) { }

    public void OnGraphicsStateChanged(PdfGraphicsState state) { }

    // ==================== PATH OPERATIONS (stubs — Task D2b-2/3) ====================

    public void FillPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd) { }

    public void StrokePath(IPathBuilder path, PdfGraphicsState state) { }

    public void FillAndStrokePath(IPathBuilder path, PdfGraphicsState state, bool evenOdd) { }

    public void SetClippingPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd) { }

    public void FillPathWithTilingPattern(IPathBuilder path, PdfGraphicsState state, bool evenOdd,
        PdfTilingPattern pattern, Action<IRenderTarget> renderPatternContent) { }

    // ==================== CONTENT OPERATIONS (stubs — Task D2b-3) ====================

    public void DrawImage(PdfImage image, PdfGraphicsState state) { }

    // ==================== MASK (stubs — Task D2b-4) ====================

    public void RenderSoftMask(string maskSubtype, Action<IRenderTarget> renderMaskContent) { }

    public void ClearSoftMask() { }
}
