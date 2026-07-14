using System.Numerics;
using PdfLibrary.Content;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Rendering;

/// <summary>
/// An IRenderTarget that records geometry-only SPI calls into a thread-agnostic PageDrawList — and
/// decodes images to RGBA — entirely OFF the UI thread (no Avalonia objects touched). DrawListBuilder
/// replays it into a DrawingGroup on the UI thread. Segments are snapshotted; state is cloned; images
/// are decoded here.
/// </summary>
public sealed class RecordingRenderTarget : IRenderTarget
{
    private readonly PdfDocument? _document;
    private readonly List<DrawCommand> _commands = [];
    private BeginPageArgs _begin = new(1, 0, 0, 1, 0, 0, 0);
    private (int w, int h, double scale) _dims;

    // Soft-mask lifecycle (mirrors PdfLibrary.Rendering.SkiaSharp SoftMaskManager): a mask set via
    // RenderSoftMask stays active until its owning save-scope exits (Q) or it is cleared to /None
    // (OnGraphicsStateChanged). The engine driver never calls ClearSoftMask, so the matching pop
    // MUST be emitted here — otherwise the replayed SaveLayers go unbalanced and corrupt the stack.
    private int _saveDepth;
    private bool _softMaskActive;
    private int _softMaskOwnerDepth = -1;

    public RecordingRenderTarget(PdfDocument? document) => _document = document;

    public static PageDrawList Record(PdfPage page, double scale)
    {
        var t = new RecordingRenderTarget(page.Document);
        page.Render(t, pageNumber: 1, scale: scale);
        return new PageDrawList(t._begin, t._commands);
    }

    /// <summary>Returns a snapshot of the commands recorded so far without running a full page render.
    /// Intended for unit-testing state-snapshot isolation: callers can drive <see cref="FillPath"/> etc.
    /// directly, then verify that mutating the original state does not affect the recorded command.</summary>
    public PageDrawList TakeSnapshot() => new PageDrawList(_begin, _commands.ToList());

    public int CurrentPageNumber { get; private set; }

    public void BeginPage(int pageNumber, double width, double height, double scale = 1.0,
        double cropOffsetX = 0, double cropOffsetY = 0, int rotation = 0)
    {
        CurrentPageNumber = pageNumber;
        _begin = new BeginPageArgs(pageNumber, width, height, scale, cropOffsetX, cropOffsetY, rotation);
        double pw = (rotation is 90 or 270 ? height : width) * scale;
        double ph = (rotation is 90 or 270 ? width : height) * scale;
        _dims = ((int)Math.Ceiling(pw), (int)Math.Ceiling(ph), scale);
    }

    public void EndPage() => PopActiveSoftMask();   // pop a mask left dangling by unbalanced content

    public void Clear()
    {
        _commands.Clear();
        CurrentPageNumber = 0;
        _saveDepth = 0;
        _softMaskActive = false;
        _softMaskOwnerDepth = -1;
    }

    public (int width, int height, double scale) GetPageDimensions() => _dims;

    private static IReadOnlyList<PathSegment> Snap(IPathBuilder path) => path.Segments.ToList();

    public void FillPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    { if (!path.IsEmpty) _commands.Add(new FillCommand(Snap(path), evenOdd, state.Clone())); }

    public void StrokePath(IPathBuilder path, PdfGraphicsState state)
    { if (!path.IsEmpty) _commands.Add(new StrokeCommand(Snap(path), state.Clone())); }

    public void FillAndStrokePath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    { if (!path.IsEmpty) _commands.Add(new FillStrokeCommand(Snap(path), evenOdd, state.Clone())); }

    public void FillPathWithTilingPattern(IPathBuilder path, PdfGraphicsState state, bool evenOdd,
        PdfTilingPattern pattern, Action<IRenderTarget> renderPatternContent)
    {
        if (path.IsEmpty) return;

        // Capture the tile content into a sub-list (like a transparency group). The tile is rendered by
        // the engine with an identity CTM, so its geometry is in pattern space; SkiaPageRenderer replays
        // it clipped to the fill path, mapped by the pattern matrix and repeated on the XStep/YStep grid.
        var sub = new RecordingRenderTarget(_document);
        sub.BeginPage(_begin.PageNumber, _begin.Width, _begin.Height, _begin.Scale,
            _begin.CropOffsetX, _begin.CropOffsetY, _begin.Rotation);
        renderPatternContent(sub);
        sub.EndPage();
        PageDrawList? content = sub._commands.Count > 0 ? new PageDrawList(sub._begin, sub._commands.ToList()) : null;

        _commands.Add(new TilingFillCommand(Snap(path), evenOdd, state.Clone(),
            content, pattern.Matrix, (float)pattern.XStep, (float)pattern.YStep,
            (float)pattern.BBox.X1, (float)pattern.BBox.Y1, (float)pattern.BBox.X2, (float)pattern.BBox.Y2));
    }

    public void SetClippingPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    { if (!path.IsEmpty) _commands.Add(new ClipCommand(Snap(path), evenOdd)); }

    public void PaintShading(ShadingDescriptor shading, PdfGraphicsState state)
    { _commands.Add(new ShadingCommand(shading, state.Clone())); }

    public void FillPathWithShadingPattern(IPathBuilder path, PdfGraphicsState state, bool evenOdd, ShadingDescriptor shading)
    { if (!path.IsEmpty) _commands.Add(new ShadingPatternFillCommand(Snap(path), evenOdd, shading, state.Clone())); }

    public void SaveState() { _saveDepth++; _commands.Add(new SaveCommand()); }

    public void RestoreState()
    {
        // A mask owned by the scope being exited is applied and popped BEFORE the state restore
        // (SoftMaskManager.OnBeforeRestore). The depth match keeps a mask alive across inner q/Q.
        if (_softMaskActive && _saveDepth == _softMaskOwnerDepth)
            PopActiveSoftMask();
        _commands.Add(new RestoreCommand());
        if (_saveDepth > 0) _saveDepth--;
    }

    public void DrawImage(PdfImage image, PdfGraphicsState state)
    {
        (byte R, byte G, byte B, byte A)? maskColor = null;
        if (image.IsImageMask)
        {
            (byte mr, byte mg, byte mb) = PdfColorToRgb.ToRgb(state.ResolvedFillColor, state.ResolvedFillColorSpace);
            maskColor = (mr, mg, mb, PdfColorToRgb.AlphaByte(state.FillAlpha));
        }
        // BPC + intent from the graphics state (PDF 2.0 /UseBlackPtComp, /RI); an image's own
        // /Intent wins over the state's — same precedence as the Skia ImageRenderer. Closes the
        // Phase-0 known-diff: ICC-profiled images previously converted with BPC off, default intent.
        PdfImageToRgba.RgbaImage? decoded = PdfImageToRgba.ToRgba(image, _document, maskColor,
            state.UseBlackPointCompensation, image.Intent ?? state.RenderingIntent);
        if (decoded is not { } img || img.Rgba.Length == 0) return;

        // Native DeviceCMYK samples (if the image resolves to CMYK) so the CMYK compositor paints native
        // ink instead of an RGB→CMYK round-trip. Only attach when the plane matches the RGBA dimensions,
        // since CompositeImage samples colour (CMYK) and alpha (RGBA) at the same pixel index.
        byte[]? cmyk = null;
        byte[]? nativeCmyk = PdfImageToCmyk.TryToCmyk(image, _document, out int cw, out int ch);
        if (nativeCmyk is not null && cw == img.Width && ch == img.Height) cmyk = nativeCmyk;

        // Overprint plate mask from the image's colour space: a DeviceN/Separation image (incl. an Indexed
        // duotone) marks only its own colorants, so basic overprint (the op flag) preserves the plates it
        // doesn't mark. DeviceCMYK/device/spot spaces → null → knockout (ISO 32000 §8.6.7; GWG010 vs GWG082).
        (bool C, bool M, bool Y, bool K)? plates = PdfImageToCmyk.PlateMaskFor(image, _document);

        // SP-6a: per-spot image ink (Separation/DeviceN image content routed to spot planes by the CMYK
        // compositor). Null for no-spot images → ImageCommand.Spots stays null (unchanged behaviour).
        SpotImageInk? spots = PdfImageToCmyk.TryToSpotInk(image, _document, out int sw, out int sh);
        if (spots is not null && (sw != img.Width || sh != img.Height)) spots = null;

        _commands.Add(new ImageCommand(img.Rgba, img.Width, img.Height, img.Alpha, state.Ctm, state.Clone(), cmyk, plates, spots));
    }

    public void ApplyCtm(Matrix3x2 ctm) { }

    public void OnGraphicsStateChanged(PdfGraphicsState state)
    {
        // /SMask /None (the current state has no mask) clears an active mask.
        if (state.SoftMask is null) PopActiveSoftMask();
    }

    public void RenderSoftMask(string maskSubtype, Action<IRenderTarget> renderMaskContent)
    {
        PopActiveSoftMask();   // replacing an active mask applies+pops it first (SoftMaskManager.SetMask)
        var sub = new RecordingRenderTarget(_document);
        sub.BeginPage(_begin.PageNumber, _begin.Width, _begin.Height, _begin.Scale,
            _begin.CropOffsetX, _begin.CropOffsetY, _begin.Rotation);
        renderMaskContent(sub);
        _commands.Add(new SoftMaskPushCommand(maskSubtype, new PageDrawList(sub._begin, sub._commands.ToList())));
        _softMaskActive = true;
        _softMaskOwnerDepth = _saveDepth;
    }

    public void ClearSoftMask() => PopActiveSoftMask();

    // A Form XObject transparency group: capture the group's content into a sub-list (like RenderSoftMask
    // captures mask content), so a compositing consumer can composite it as a unit. Crucially this ISOLATES
    // the group's inner soft-mask lifecycle from the main recorder — the form's inner `/SMask /None` now pops
    // in the sub-recorder instead of prematurely clearing the OUTER mask that should mask the whole group.
    public void RenderTransparencyGroup(TransparencyGroupInfo info, Action<IRenderTarget> renderContent)
    {
        var sub = new RecordingRenderTarget(_document);
        sub.BeginPage(_begin.PageNumber, _begin.Width, _begin.Height, _begin.Scale,
            _begin.CropOffsetX, _begin.CropOffsetY, _begin.Rotation);
        renderContent(sub);
        sub.EndPage();   // pop any mask left dangling by unbalanced group content
        _commands.Add(new GroupCommand(info, new PageDrawList(sub._begin, sub._commands.ToList())));
    }

    private void PopActiveSoftMask()
    {
        if (!_softMaskActive) return;
        _commands.Add(new SoftMaskPopCommand());
        _softMaskActive = false;
        _softMaskOwnerDepth = -1;
    }
}
