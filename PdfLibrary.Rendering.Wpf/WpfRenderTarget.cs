using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfLibrary.Content;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

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

    private readonly PdfDocument? _document;

    /// <summary>
    /// Initialises the render target, optionally binding it to a <see cref="PdfDocument"/>
    /// so that image decoding can resolve indirect references (ICC profiles, SMasks, etc.).
    /// </summary>
    public WpfRenderTarget(PdfDocument? document = null)
    {
        _document = document;
    }

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
        while (_pushDepth > 0) { _dc.Pop(); _pushDepth--; }

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

    // ==================== PATH OPERATIONS ====================

    private static Geometry BuildGeometry(IPathBuilder path, bool evenOdd)
    {
        var pg = new PathGeometry { FillRule = evenOdd ? FillRule.EvenOdd : FillRule.Nonzero };
        PathFigure? figure = null;

        foreach (PathSegment seg in path.Segments)
        {
            switch (seg)
            {
                case MoveToSegment m:
                    figure = new PathFigure { StartPoint = new Point(m.X, m.Y), IsFilled = true, IsClosed = false };
                    pg.Figures.Add(figure);
                    break;
                case LineToSegment l:
                    figure?.Segments.Add(new System.Windows.Media.LineSegment(new Point(l.X, l.Y), isStroked: true));
                    break;
                case CurveToSegment c:
                    figure?.Segments.Add(new BezierSegment(
                        new Point(c.X1, c.Y1), new Point(c.X2, c.Y2), new Point(c.X3, c.Y3), isStroked: true));
                    break;
                case ClosePathSegment:
                    if (figure is not null) figure.IsClosed = true;
                    figure = null;
                    break;
            }
        }

        pg.Freeze();
        return pg;
    }

    public void FillPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        if (path.IsEmpty || _dc is null) return;
        (byte r, byte g, byte b) = PdfColorToRgb.ToRgb(state.ResolvedFillColor, state.ResolvedFillColorSpace);
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b)) { Opacity = state.FillAlpha };
        brush.Freeze();
        _dc.DrawGeometry(brush, null, BuildGeometry(path, evenOdd));
    }

    public void StrokePath(IPathBuilder path, PdfGraphicsState state)
    {
        if (path.IsEmpty || _dc is null) return;
        (byte r, byte g, byte b) = PdfColorToRgb.ToRgb(state.ResolvedStrokeColor, state.ResolvedStrokeColorSpace);
        double ctmScale = Math.Sqrt(Math.Abs(state.Ctm.M11 * state.Ctm.M22 - state.Ctm.M12 * state.Ctm.M21));
        double width = Math.Max(state.LineWidth * ctmScale, 0.1);
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b)) { Opacity = state.StrokeAlpha };
        brush.Freeze();
        var pen = new Pen(brush, width)
        {
            StartLineCap = Cap(state.LineCap),
            EndLineCap   = Cap(state.LineCap),
            LineJoin      = Join(state.LineJoin),
            MiterLimit    = state.MiterLimit
        };
        if (state.DashPattern is { Length: > 0 })
            pen.DashStyle = new DashStyle(
                state.DashPattern.Select(d => d * ctmScale / width),
                state.DashPhase * ctmScale / width);
        pen.Freeze();
        _dc.DrawGeometry(null, pen, BuildGeometry(path, evenOdd: false));
    }

    public void FillAndStrokePath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        FillPath(path, state, evenOdd);
        StrokePath(path, state);
    }

    private static PenLineCap  Cap(int c) => c switch { 1 => PenLineCap.Round, 2 => PenLineCap.Square, _ => PenLineCap.Flat };
    private static PenLineJoin Join(int j) => j switch { 1 => PenLineJoin.Round, 2 => PenLineJoin.Bevel, _ => PenLineJoin.Miter };

    public void SetClippingPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        if (path.IsEmpty || _dc is null) return;
        _dc.PushClip(BuildGeometry(path, evenOdd));
        _pushDepth++;
    }

    public void FillPathWithTilingPattern(IPathBuilder path, PdfGraphicsState state, bool evenOdd,
        PdfTilingPattern pattern, Action<IRenderTarget> renderPatternContent)
        // Approximate: render a solid fill using the current fill colour (same as the SVG target).
        => FillPath(path, state, evenOdd);

    // ==================== CONTENT OPERATIONS ====================

    public void DrawImage(PdfImage image, PdfGraphicsState state)
    {
        if (_dc is null) return;

        // Decode to RGBA8888 (R,G,B,A order, top-row-first) — returns null for unsupported images.
        PdfImageToRgba.RgbaImage? decoded = PdfImageToRgba.ToRgba(image, _document);
        if (decoded is not { } img || img.Rgba.Length == 0) return;

        // Swap R↔B to produce BGRA (WPF's native channel order).
        // For Premultiplied alpha, pre-multiply RGB by A before the swap.
        bool premul = img.Alpha == AlphaMode.Premultiplied;
        byte[] bgra = new byte[img.Rgba.Length];
        for (int i = 0; i < img.Rgba.Length; i += 4)
        {
            byte r = img.Rgba[i];
            byte g = img.Rgba[i + 1];
            byte b = img.Rgba[i + 2];
            byte a = img.Rgba[i + 3];
            if (premul)
            {
                r = (byte)(r * a / 255);
                g = (byte)(g * a / 255);
                b = (byte)(b * a / 255);
            }
            bgra[i]     = b;
            bgra[i + 1] = g;
            bgra[i + 2] = r;
            bgra[i + 3] = a;
        }

        PixelFormat fmt = premul ? PixelFormats.Pbgra32 : PixelFormats.Bgra32;
        BitmapSource bs = BitmapSource.Create(img.Width, img.Height, 96, 96, fmt, null, bgra, img.Width * 4);
        bs.Freeze();

        // Draw in the unit square with:
        //   1. state.Ctm   — maps image unit square → PDF user space
        //   2. Y-flip      — PDF image rows are top-first; WPF's Y axis goes down
        // These two transforms are local to this draw call; Pop() them immediately
        // so they do NOT affect _pushDepth (which tracks Save/Clip state).
        Matrix3x2 c = state.Ctm;
        var ctmMatrix = new Matrix(c.M11, c.M12, c.M21, c.M22, c.M31, c.M32);
        _dc.PushTransform(new MatrixTransform(ctmMatrix));
        _dc.PushTransform(new MatrixTransform(1, 0, 0, -1, 0, 1));   // translate(0,1) scale(1,-1)
        _dc.DrawImage(bs, new Rect(0, 0, 1, 1));
        _dc.Pop();
        _dc.Pop();
    }

    // ==================== MASK (stubs — Task D2b-4) ====================

    public void RenderSoftMask(string maskSubtype, Action<IRenderTarget> renderMaskContent) { }

    public void ClearSoftMask() { }
}
