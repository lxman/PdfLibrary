using System.Numerics;
using Logging;
using PdfLibrary.Content;
using PdfLibrary.Rendering.SkiaSharp.Conversion;
using SkiaSharp;

namespace PdfLibrary.Rendering.SkiaSharp.Rendering;

/// <summary>
/// Holds an isolated transparency group snapshot pending composite after canvas restore.
/// </summary>
internal class PendingComposite
{
    public SKImage GroupSnapshot { get; set; } = null!;
    public SKPoint Position { get; set; }
}

/// <summary>
/// Handles path rendering operations for PDF content.
/// Responsible for stroking, filling, clipping, and pattern rendering with paths.
/// </summary>
internal class PathRenderer
{
    private readonly SKCanvas _canvas;
    private readonly Func<Matrix3x2> _getInitialTransform;
    private readonly SKSurface _surface;
    private readonly double _scale;
    private readonly PatternRenderer _patternRenderer;
    private PendingComposite? _pendingComposite;

    /// <summary>
    /// Creates a new PathRenderer.
    /// </summary>
    /// <param name="canvas">The SKCanvas to draw on</param>
    /// <param name="getInitialTransform">Function that returns the current initial transformation matrix for display (Y-flip + crop offset)</param>
    /// <param name="surface">The SKSurface for creating isolated transparency groups</param>
    /// <param name="scale">Rendering scale factor</param>
    public PathRenderer(SKCanvas canvas, Func<Matrix3x2> getInitialTransform, SKSurface surface, double scale)
    {
        _canvas = canvas;
        _getInitialTransform = getInitialTransform;
        _surface = surface;
        _scale = scale;
        _patternRenderer = new PatternRenderer(canvas, getInitialTransform, scale);
    }

    public void StrokePath(IPathBuilder path, PdfGraphicsState state)
    {
        if (path.IsEmpty)
            return;

        _canvas.Save();
        try
        {
            // Apply transformation matrix
            ApplyPathTransformationMatrix(state);

            // Convert IPathBuilder to SKPath
            SKPath skPath = PathConverter.ConvertToSkPath(path);

            // Create stroke paint with alpha from graphics state
            SKColor strokeColor = ColorConverter.ConvertColor(state.ResolvedStrokeColor, state.ResolvedStrokeColorSpace);
            strokeColor = ColorConverter.ApplyAlpha(strokeColor, state.StrokeAlpha);

            // Calculate CTM scaling factor for line width
            // The line width should be scaled by the CTM's linear scaling factor
            // which is the square root of the absolute determinant of the 2x2 portion
            Matrix3x2 ctm = state.Ctm;
            double ctmScale = Math.Sqrt(Math.Abs(ctm.M11 * ctm.M22 - ctm.M12 * ctm.M21));
            // Note: We DO want small scale factors - they correctly scale large PDF line widths to device pixels
            // Only clamp to a minimum to avoid extremely thin lines (less than 0.5 device pixel)
            double scaledLineWidth = state.LineWidth * ctmScale;
            if (scaledLineWidth < 0.5) scaledLineWidth = 0.5; // Minimum 0.5 device pixel line width

            using var paint = new SKPaint();
            paint.Color = strokeColor;
            paint.IsAntialias = true;
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = (float)scaledLineWidth;
            paint.StrokeCap = ConvertLineCap(state.LineCap);
            paint.StrokeJoin = ConvertLineJoin(state.LineJoin);
            paint.StrokeMiter = (float)state.MiterLimit;

            // Apply blend mode (including overprint simulation)
            paint.BlendMode = BlendModeConverter.GetBlendModeForState(state, useStrokeOverprint: true);

            // Apply a dash pattern if present (scale by CTM as well)
            if (state.DashPattern is not null && state.DashPattern.Length > 0)
            {
                float[] dashIntervals = state.DashPattern.Select(d => (float)(d * ctmScale)).ToArray();
                // Ensure dash intervals are at least 0.5 pixels
                for (var i = 0; i < dashIntervals.Length; i++)
                    if (dashIntervals[i] < 0.5f) dashIntervals[i] = 0.5f;
                paint.PathEffect = SKPathEffect.CreateDash(dashIntervals, (float)(state.DashPhase * ctmScale));
            }

            _canvas.DrawPath(skPath, paint);
            skPath.Dispose();
        }
        finally
        {
            _canvas.Restore();
        }
    }

    public void FillPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        if (path.IsEmpty)
            return;

        _canvas.Save();
        try
        {
            // Apply transformation matrix
            ApplyPathTransformationMatrix(state);

            // Convert IPathBuilder to SKPath
            SKPath skPath = PathConverter.ConvertToSkPath(path);

            // Set fill rule
            // Set fill type based on PDF operator (evenOdd parameter)
            // Previously we forced EvenOdd when Y-flip was detected, but this causes overlapping
            // paths to create holes. The Y-flip is just a coordinate transform and doesn't
            // require changing the fill rule.
            skPath.FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;

            // Create fill paint with alpha from graphics state
            SKColor fillColor = ColorConverter.ConvertColor(state.ResolvedFillColor, state.ResolvedFillColorSpace);
            fillColor = ColorConverter.ApplyAlpha(fillColor, state.FillAlpha);

            PdfLogger.Log(LogCategory.Graphics, $"FILL COLOR: ColorSpace={state.ResolvedFillColorSpace}, Color=({fillColor.Red},{fillColor.Green},{fillColor.Blue}), Alpha={fillColor.Alpha}");

            // Determine blend mode using utility
            SKBlendMode blendMode = BlendModeConverter.GetBlendModeForState(state, useStrokeOverprint: false);
            PdfLogger.Log(LogCategory.Graphics, $"BLEND MODE: PDF BlendMode='{state.BlendMode}', SKBlendMode={blendMode}, FillOverprint={state.FillOverprint}");

            // For non-normal blend modes, use isolated transparency groups
            if (blendMode != SKBlendMode.SrcOver)
            {
                PdfLogger.Log(LogCategory.Graphics, $"ISOLATED GROUP: Using isolated transparency group for blend mode {blendMode}");

                // Get path bounds in user space and transform to device space
                SKRect userSpaceBounds = skPath.Bounds;
                SKMatrix canvasMatrix = _canvas.TotalMatrix;
                SKRect pathBounds = canvasMatrix.MapRect(userSpaceBounds);
                pathBounds.Inflate(2, 2); // Margin for antialiasing

                PdfLogger.Log(LogCategory.Graphics, $"ISOLATED GROUP: pathBounds=({pathBounds.Left},{pathBounds.Top},{pathBounds.Width},{pathBounds.Height})");

                // Create isolated group
                var groupInfo = new SKImageInfo(
                    (int)Math.Ceiling(pathBounds.Width),
                    (int)Math.Ceiling(pathBounds.Height),
                    SKColorType.Rgba8888,
                    SKAlphaType.Premul);

                using var groupSurface = SKSurface.Create(groupInfo);
                if (groupSurface != null)
                {
                    SKCanvas? groupCanvas = groupSurface.Canvas;
                    // CRITICAL: Isolated transparency groups must have TRANSPARENT backdrop per PDF spec
                    // (ISO 32000, Section 11.4.5). Blend modes operate against transparency, not white.
                    // The group result is then composited over the page background.
                    groupCanvas.Clear(SKColors.Transparent);

                    // Draw backdrop (existing canvas content) into the isolated group
                    // Draw at negative offset so the pathBounds region appears at (0,0) in the group
                    using SKImage? canvasSnapshot = _surface.Snapshot();
                    groupCanvas.DrawImage(canvasSnapshot, -pathBounds.Left, -pathBounds.Top);

                    // Apply the same transformation matrix as main canvas, then offset for the group bounds
                    // This transforms the user-space path to device space, then positions it correctly in the group
                    //
                    // IMPORTANT: We can't use SetMatrix + Translate because Translate is affected by the matrix's Y-scale.
                    // Instead, manually construct the correct matrix:
                    // - canvasMatrix transforms from user space to device space (includes Y-flip)
                    // - We want to translate by (-pathBounds.Left, -pathBounds.Top) in DEVICE space
                    // - The correct matrix combines these: first transform, then translate in device space
                    var correctedMatrix = new SKMatrix
                    {
                        ScaleX = canvasMatrix.ScaleX,
                        SkewX = canvasMatrix.SkewX,
                        SkewY = canvasMatrix.SkewY,
                        ScaleY = canvasMatrix.ScaleY,
                        TransX = canvasMatrix.TransX - pathBounds.Left,
                        TransY = canvasMatrix.TransY - pathBounds.Top,
                        Persp0 = canvasMatrix.Persp0,
                        Persp1 = canvasMatrix.Persp1,
                        Persp2 = canvasMatrix.Persp2
                    };

                    PdfLogger.Log(LogCategory.Graphics, $"ISOLATED GROUP: Main canvas matrix: [{canvasMatrix.ScaleX},{canvasMatrix.SkewX},{canvasMatrix.SkewY},{canvasMatrix.ScaleY},{canvasMatrix.TransX},{canvasMatrix.TransY}]");
                    PdfLogger.Log(LogCategory.Graphics, $"ISOLATED GROUP: Corrected matrix: [{correctedMatrix.ScaleX},{correctedMatrix.SkewX},{correctedMatrix.SkewY},{correctedMatrix.ScaleY},{correctedMatrix.TransX},{correctedMatrix.TransY}]");

                    groupCanvas.SetMatrix(correctedMatrix);
                    PdfLogger.Log(LogCategory.Graphics, $"ISOLATED GROUP: Drawing shape with color ({fillColor.Red},{fillColor.Green},{fillColor.Blue},{fillColor.Alpha}), blendMode={blendMode}");
                    PdfLogger.Log(LogCategory.Graphics, $"ISOLATED GROUP: Path bounds in user space: {skPath.Bounds}");

                    // Draw shape with blend mode into the group
                    using var paint = new SKPaint();
                    paint.Color = fillColor;
                    paint.IsAntialias = true;
                    paint.Style = SKPaintStyle.Fill;
                    paint.BlendMode = blendMode;
                    groupCanvas.DrawPath(skPath, paint);

                    // Flush to ensure drawing is complete
                    groupCanvas.Flush();

                    PdfLogger.Log(LogCategory.Graphics, "ISOLATED GROUP: Shape drawn, compositing back to main canvas");

                    // Composite the group result back with Normal blend
                    // NOTE: Don't use 'using' here because we need to keep the snapshot alive
                    // for the pending composite that happens after the canvas restore.
                    SKImage? groupSnapshot = groupSurface.Snapshot();

                    // CRITICAL FIX: We need to draw the composite OUTSIDE the save/restore block
                    // because any clipping or transformation state might interfere.
                    // Save the group snapshot for later composite after the restore.
                    // The snapshot will be disposed after it's used in the finally block.
                    _pendingComposite = new PendingComposite
                    {
                        GroupSnapshot = groupSnapshot,
                        Position = new SKPoint(pathBounds.Left, pathBounds.Top)
                    };
                    PdfLogger.Log(LogCategory.Graphics, $"ISOLATED GROUP: Scheduled composite at ({pathBounds.Left}, {pathBounds.Top}) for after restore");
                }
                else
                {
                    // Fallback
                    using var paint = new SKPaint();
                    paint.Color = fillColor;
                    paint.IsAntialias = true;
                    paint.Style = SKPaintStyle.Fill;
                    paint.BlendMode = blendMode;
                    _canvas.DrawPath(skPath, paint);
                }
            }
            else
            {
                // Normal blend - draw directly
                using var paint = new SKPaint();
                paint.Color = fillColor;
                paint.IsAntialias = true;
                paint.Style = SKPaintStyle.Fill;
                _canvas.DrawPath(skPath, paint);
            }

            skPath.Dispose();
        }
        finally
        {
            _canvas.Restore();

            // Apply any pending composite AFTER restore to avoid clipping/transform interference
            if (_pendingComposite != null)
            {
                try
                {
                    _canvas.Save();
                    _canvas.SetMatrix(SKMatrix.Identity);

                    using var compositePaint = new SKPaint
                    {
                        BlendMode = SKBlendMode.SrcOver,
                        IsAntialias = true
                    };
                    _canvas.DrawImage(_pendingComposite.GroupSnapshot, _pendingComposite.Position.X, _pendingComposite.Position.Y, compositePaint);
                    _canvas.Flush();

                    PdfLogger.Log(LogCategory.Graphics, $"ISOLATED GROUP: Composited at ({_pendingComposite.Position.X}, {_pendingComposite.Position.Y}) AFTER restore");

                    _canvas.Restore();

                    _pendingComposite.GroupSnapshot.Dispose();
                    _pendingComposite = null;
                }
                catch (Exception ex)
                {
                    PdfLogger.Log(LogCategory.Graphics, $"ISOLATED GROUP: Failed to composite after restore: {ex.Message}");
                }
            }
        }
    }

    public void FillPathWithTilingPattern(IPathBuilder path, PdfGraphicsState state, bool evenOdd,
        PdfTilingPattern pattern, Action<IRenderTarget> renderPatternContent)
    {
        if (path.IsEmpty)
            return;

        _canvas.Save();
        try
        {
            // Apply transformation matrix
            ApplyPathTransformationMatrix(state);

            // Convert IPathBuilder to SKPath
            SKPath skPath = PathConverter.ConvertToSkPath(path);
            // Set fill type based on PDF operator (evenOdd parameter)
            skPath.FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;

            // Delegate pattern rendering to PatternRenderer
            _patternRenderer.FillPathWithTilingPattern(skPath, state, pattern, renderPatternContent);

            skPath.Dispose();
        }
        finally
        {
            _canvas.Restore();
        }
    }

    public void FillAndStrokePath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        if (path.IsEmpty)
            return;

        _canvas.Save();
        try
        {
            // Apply transformation matrix (once for both operations)
            ApplyPathTransformationMatrix(state);

            // Convert IPathBuilder to SKPath
            SKPath skPath = PathConverter.ConvertToSkPath(path);

            // Set fill rule
            // Set fill type based on PDF operator (evenOdd parameter)
            // Previously we forced EvenOdd when Y-flip was detected, but this causes overlapping
            // paths to create holes. The Y-flip is just a coordinate transform and doesn't
            // require changing the fill rule.
            skPath.FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;

            // Fill first (with fill alpha from graphics state)
            SKColor fillColor = ColorConverter.ConvertColor(state.ResolvedFillColor, state.ResolvedFillColorSpace);
            fillColor = ColorConverter.ApplyAlpha(fillColor, state.FillAlpha);
            using (var fillPaint = new SKPaint())
            {
                fillPaint.Color = fillColor;
                fillPaint.IsAntialias = true;
                fillPaint.Style = SKPaintStyle.Fill;
                _canvas.DrawPath(skPath, fillPaint);
            }

            // Then stroke (with stroke alpha from graphics state)
            SKColor strokeColor = ColorConverter.ConvertColor(state.ResolvedStrokeColor, state.ResolvedStrokeColorSpace);
            strokeColor = ColorConverter.ApplyAlpha(strokeColor, state.StrokeAlpha);

            // Calculate CTM scaling factor for line width
            Matrix3x2 ctm = state.Ctm;
            double ctmScale = Math.Sqrt(Math.Abs(ctm.M11 * ctm.M22 - ctm.M12 * ctm.M21));
            // Note: Small scale factors are valid - they correctly scale large PDF values to device pixels
            double scaledLineWidth = state.LineWidth * ctmScale;
            if (scaledLineWidth < 0.5) scaledLineWidth = 0.5; // Minimum 0.5 device pixel line width

            using (var strokePaint = new SKPaint())
            {
                strokePaint.Color = strokeColor;
                strokePaint.IsAntialias = true;
                strokePaint.Style = SKPaintStyle.Stroke;
                strokePaint.StrokeWidth = (float)scaledLineWidth;
                strokePaint.StrokeCap = ConvertLineCap(state.LineCap);
                strokePaint.StrokeJoin = ConvertLineJoin(state.LineJoin);
                strokePaint.StrokeMiter = (float)state.MiterLimit;
                // Apply a dash pattern if present (scale by CTM as well)
                if (state.DashPattern is not null && state.DashPattern.Length > 0)
                {
                    float[] dashIntervals = state.DashPattern.Select(d => (float)(d * ctmScale)).ToArray();
                    // Ensure dash intervals are at least 0.5 pixels
                    for (var i = 0; i < dashIntervals.Length; i++)
                        if (dashIntervals[i] < 0.5f) dashIntervals[i] = 0.5f;
                    strokePaint.PathEffect = SKPathEffect.CreateDash(dashIntervals, (float)(state.DashPhase * ctmScale));
                }

                _canvas.DrawPath(skPath, strokePaint);
            }

            skPath.Dispose();
        }
        finally
        {
            _canvas.Restore();
        }
    }

    public void SetClippingPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        if (path.IsEmpty)
            return;

        // Convert IPathBuilder to SKPath
        // The path coordinates are ALREADY transformed by CTM during construction (same as fill/stroke paths)
        SKPath skPath = PathConverter.ConvertToSkPath(path);

        // Set fill rule for clipping
        skPath.FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;

        // IMPORTANT: Clipping paths are "frozen" in device coordinates when ClipPath is called.
        // The canvas matrix at the time of clipping transforms the path to device space.
        // We MUST use the exact same initial transform that BeginPage uses (including CropBox offset),
        // so that the clip boundary aligns exactly with drawn content.
        //
        // Initial transform includes:
        // 1. Translate by (-cropOffsetX, -cropOffsetY) - shift so CropBox origin is at (0,0)
        // 2. Scale by (scale, -scale) - scales content and flips Y
        // 3. Translate by (0, height * scale) - moves origin to top-left of scaled output
        Matrix3x2 initialTransform = _getInitialTransform();
        var displayMatrix = new SKMatrix(
            initialTransform.M11, initialTransform.M21, initialTransform.M31,
            initialTransform.M12, initialTransform.M22, initialTransform.M32,
            0, 0, 1
        );
        skPath.Transform(displayMatrix);

        // Save current canvas state, set identity, apply clip, restore
        // This ensures the clip path (now in device coords) is applied without further transformation
        SKMatrix currentMatrix = _canvas.TotalMatrix;
        _canvas.SetMatrix(SKMatrix.Identity);

        // Apply the clipping path with anti-aliasing disabled to get sharp clip edges
        _canvas.ClipPath(skPath);

        // Restore canvas matrix for subsequent drawing operations
        _canvas.SetMatrix(currentMatrix);

        skPath.Dispose();
    }

    // ==================== HELPER METHODS ====================

    /// <summary>
    /// Apply transformation matrix for path operations
    /// Per the code comments, paths are already transformed by CTM during construction.
    /// We only need to apply the display matrix (Y-flip with crop offset) to convert from PDF to screen coordinates.
    /// </summary>
    private void ApplyPathTransformationMatrix(PdfGraphicsState state)
    {
        // Paths are already transformed by CTM during construction (in PDF user space)
        // We need to apply ONLY the initial transform (Y-flip + crop offset for PDF→screen conversion)
        // NOT the full CTM, otherwise we'd be transforming twice
        //
        // The initial transform correctly handles:
        // 1. CropBox offset translation (-cropOffsetX, -cropOffsetY)
        // 2. Y-axis flip with scale
        // 3. Translation to put origin at top-left

        Matrix3x2 initialTransform = _getInitialTransform();
        var displayMatrix = new SKMatrix(
            initialTransform.M11, initialTransform.M21, initialTransform.M31,
            initialTransform.M12, initialTransform.M22, initialTransform.M32,
            0, 0, 1
        );

        _canvas.SetMatrix(displayMatrix);
    }

    /// <summary>
    /// Convert PDF line cap style to SkiaSharp
    /// </summary>
    private SKStrokeCap ConvertLineCap(int lineCap)
    {
        return lineCap switch
        {
            0 => SKStrokeCap.Butt,      // Butt cap
            1 => SKStrokeCap.Round,     // Round cap
            2 => SKStrokeCap.Square,    // Projecting square cap
            _ => SKStrokeCap.Butt
        };
    }

    /// <summary>
    /// Convert PDF line join style to SkiaSharp
    /// </summary>
    private SKStrokeJoin ConvertLineJoin(int lineJoin)
    {
        return lineJoin switch
        {
            0 => SKStrokeJoin.Miter,    // Miter join
            1 => SKStrokeJoin.Round,    // Round join
            2 => SKStrokeJoin.Bevel,    // Bevel join
            _ => SKStrokeJoin.Miter
        };
    }
}
