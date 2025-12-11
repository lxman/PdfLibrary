using System.Numerics;
using Logging;
using PdfLibrary.Content;
using PdfLibrary.Fonts;
using PdfLibrary.Rendering;
using PdfLibrary.Rendering.SkiaSharp.Conversion;
using SkiaSharp;

namespace PdfLibrary.Rendering.SkiaSharp.Rendering;

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

            // Apply overprint blend mode if enabled
            if (state.StrokeOverprint)
            {
                paint.BlendMode = SKBlendMode.Multiply;
            }

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

            // Determine blend mode
            SKBlendMode blendMode = SKBlendMode.SrcOver;
            if (!string.IsNullOrEmpty(state.BlendMode) && state.BlendMode != "Normal")
            {
                blendMode = ConvertBlendMode(state.BlendMode);
            }
            else if (state.FillOverprint)
            {
                blendMode = SKBlendMode.Multiply;
            }

            // For non-normal blend modes, use isolated transparency groups
            if (blendMode != SKBlendMode.SrcOver)
            {
                // Get path bounds for the isolated group
                SKRect pathBounds = skPath.Bounds;
                pathBounds.Inflate(2, 2); // Margin for antialiasing

                // Create isolated group
                var groupInfo = new SKImageInfo(
                    (int)Math.Ceiling(pathBounds.Width),
                    (int)Math.Ceiling(pathBounds.Height),
                    SKColorType.Rgba8888,
                    SKAlphaType.Premul);

                using var groupSurface = SKSurface.Create(groupInfo);
                if (groupSurface != null)
                {
                    var groupCanvas = groupSurface.Canvas;
                    groupCanvas.Clear(SKColors.Transparent);

                    // Translate to draw at origin of isolated group
                    groupCanvas.Translate(-pathBounds.Left, -pathBounds.Top);

                    // Draw backdrop (existing canvas content) into the group
                    using var canvasSnapshot = _surface.Snapshot();
                    groupCanvas.DrawImage(canvasSnapshot, 0, 0);

                    // Draw shape with blend mode into the group
                    using var paint = new SKPaint
                    {
                        Color = fillColor,
                        IsAntialias = true,
                        Style = SKPaintStyle.Fill,
                        BlendMode = blendMode
                    };
                    groupCanvas.DrawPath(skPath, paint);

                    // Composite the group result back with Normal blend
                    using var groupSnapshot = groupSurface.Snapshot();
                    _canvas.DrawImage(groupSnapshot, pathBounds.Left, pathBounds.Top);
                }
                else
                {
                    // Fallback
                    using var paint = new SKPaint
                    {
                        Color = fillColor,
                        IsAntialias = true,
                        Style = SKPaintStyle.Fill,
                        BlendMode = blendMode
                    };
                    _canvas.DrawPath(skPath, paint);
                }
            }
            else
            {
                // Normal blend - draw directly
                using var paint = new SKPaint
                {
                    Color = fillColor,
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill
                };
                _canvas.DrawPath(skPath, paint);
            }

            skPath.Dispose();
        }
        finally
        {
            _canvas.Restore();
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
            // Previously we forced EvenOdd when Y-flip was detected, but this causes overlapping
            // paths to create holes. The Y-flip is just a coordinate transform and doesn't
            // require changing the fill rule.
            skPath.FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;

            // Calculate pattern cell size in device pixels
            // Pattern BBox defines the content area, XStep/YStep define the tiling interval
            double patternWidth = Math.Abs(pattern.XStep);
            double patternHeight = Math.Abs(pattern.YStep);

            if (patternWidth <= 0) patternWidth = pattern.BBox.Width;
            if (patternHeight <= 0) patternHeight = pattern.BBox.Height;

            // Apply pattern matrix and current CTM to get device pixel size
            // Pattern matrix transforms pattern space to user space
            Matrix3x2 patternMatrix = pattern.Matrix;
            Matrix3x2 ctm = state.Ctm;
            Matrix3x2 combined = patternMatrix * ctm;

            // Calculate scale from combined matrix
            double scaleX = Math.Sqrt(combined.M11 * combined.M11 + combined.M12 * combined.M12);
            double scaleY = Math.Sqrt(combined.M21 * combined.M21 + combined.M22 * combined.M22);

            int tileWidth = Math.Max(1, (int)Math.Ceiling(patternWidth * scaleX * _scale));
            int tileHeight = Math.Max(1, (int)Math.Ceiling(patternHeight * scaleY * _scale));

            // Limit tile size to prevent memory issues
            const int maxTileSize = 2048;
            if (tileWidth > maxTileSize) tileWidth = maxTileSize;
            if (tileHeight > maxTileSize) tileHeight = maxTileSize;

            PdfLogger.Log(LogCategory.Graphics, $"PATTERN TILE: BBox={pattern.BBox}, XStep={pattern.XStep}, YStep={pattern.YStep}, tileSize={tileWidth}x{tileHeight}");

            // Create offscreen surface for pattern tile
            var tileInfo = new SKImageInfo(tileWidth, tileHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var tileSurface = SKSurface.Create(tileInfo);

            if (tileSurface is null)
            {
                PdfLogger.Log(LogCategory.Graphics, $"PATTERN: Failed to create tile surface {tileWidth}x{tileHeight}");
                skPath.Dispose();
                return;
            }

            SKCanvas tileCanvas = tileSurface.Canvas;

            // Clear with transparent (for colored patterns) or white (could vary)
            tileCanvas.Clear(SKColors.Transparent);

            // Set up transformation for pattern content
            // Pattern content is defined in pattern space (PDF coordinates with Y-up)
            // We need to transform from pattern space to tile surface space (SkiaSharp Y-down)
            tileCanvas.Save();

            // Scale from pattern space to device pixels, with Y-flip for coordinate system conversion
            var deviceScaleX = (float)(tileWidth / patternWidth);
            var deviceScaleY = (float)(tileHeight / patternHeight);

            // Apply Y-flip transformation: PDF Y-up to SkiaSharp Y-down
            // 1. Translate origin to bottom of tile
            // 2. Flip Y axis (negative scale)
            // 3. Translate to BBox origin
            tileCanvas.Translate(0, tileHeight);
            tileCanvas.Scale(deviceScaleX, -deviceScaleY);

            // Translate to account for BBox origin (pattern content is relative to BBox)
            tileCanvas.Translate(-(float)pattern.BBox.X1, -(float)pattern.BBox.Y1);

            // Create a sub-render target for the pattern content
            using var subTarget = new SkiaSharpRenderTargetForPattern(tileSurface, patternWidth, patternHeight);

            // Render pattern content
            renderPatternContent(subTarget);

            tileCanvas.Restore();

            // Get the tile as an image
            using SKImage? tileImage = tileSurface.Snapshot();
            if (tileImage is null)
            {
                PdfLogger.Log(LogCategory.Graphics, "PATTERN: Failed to snapshot tile");
                skPath.Dispose();
                return;
            }

            // Create a shader that tiles the pattern image
            // The shader maps from user space coordinates to tile bitmap coordinates
            // Shader operates in pre-canvas-transform space (user space)

            // In user space, pattern tiles every XStep * patternMatrix.M11 in X
            // and YStep * patternMatrix.M22 in Y
            float patternScaleX = Math.Abs(pattern.Matrix.M11);
            float patternScaleY = Math.Abs(pattern.Matrix.M22);
            if (patternScaleX < 0.001f) patternScaleX = 1.0f;
            if (patternScaleY < 0.001f) patternScaleY = 1.0f;

            var userStepX = (float)(patternWidth * patternScaleX);
            var userStepY = (float)(patternHeight * patternScaleY);

            // The shader needs to transform user coordinates to tile bitmap coordinates.
            // The tile bitmap was rendered with Y-flip (PDF Y-up → bitmap Y-down).
            // The canvas also has Y-flip (device Y-down → user Y-up via canvasInverse).
            // When we compose shaderMatrix with canvasInverse, these Y-flips must cancel out.
            // So shaderMatrix needs NEGATIVE Y scale to compensate for canvasInverse's flip.

            float scaleToTileX = tileWidth / userStepX;
            float scaleToTileY = tileHeight / userStepY;

            // Pattern origin in user space (from pattern matrix translation)
            float patternOriginX = pattern.Matrix.M31;
            float patternOriginY = pattern.Matrix.M32;

            // For X: sample_x = (user_x - patternOriginX) * scaleToTileX
            // For Y: We need to flip because tile was Y-flipped during rendering.
            //        tile_y = tileHeight - (user_y - patternOriginY) * scaleToTileY
            //        This equals: -user_y * scaleToTileY + (tileHeight + patternOriginY * scaleToTileY)
            // Using negative Y scale compensates for canvasInverse's Y flip.

            var shaderMatrix = SKMatrix.CreateScale(scaleToTileX, -scaleToTileY);
            shaderMatrix = shaderMatrix.PostConcat(SKMatrix.CreateTranslation(
                -patternOriginX * scaleToTileX,
                tileHeight + patternOriginY * scaleToTileY));

            // IMPORTANT: SkiaSharp shaders are sampled in DEVICE coordinates (after canvas transform)
            // The canvas transform converts user space to device space.
            // The shader's local matrix needs to transform FROM device coords TO tile bitmap coords.
            //
            // device = canvasMatrix * user
            // So: user = inverse(canvasMatrix) * device
            // And: tile = shaderMatrix * user = shaderMatrix * inverse(canvasMatrix) * device
            //
            // Therefore, the effective shader matrix for device coords is:
            // effectiveShaderMatrix = shaderMatrix * inverse(canvasMatrix)

            SKMatrix canvasMatrix = _canvas.TotalMatrix;

            // Get the inverse of canvas matrix to transform from device to user space
            if (!canvasMatrix.TryInvert(out SKMatrix canvasInverse))
            {
                PdfLogger.Log(LogCategory.Graphics, "PATTERN SHADER: Canvas matrix not invertible, skipping pattern");
                skPath.Dispose();
                return;
            }

            // Effective shader matrix = shaderMatrix (user→tile) * canvasInverse (device→user)
            // This gives us: device→user→tile
            SKMatrix effectiveShaderMatrix = shaderMatrix.PreConcat(canvasInverse);

            PdfLogger.Log(LogCategory.Graphics, $"PATTERN SHADER: tile={tileWidth}x{tileHeight}, origin=({patternOriginX:F1},{patternOriginY:F1}), step=({userStepX:F1},{userStepY:F1})");

            // Create bitmap from the tile image
            using SKBitmap? tileBitmap = SKBitmap.FromImage(tileImage);
            if (tileBitmap is null)
            {
                PdfLogger.Log(LogCategory.Graphics, "PATTERN SHADER: Failed to create bitmap from tile image");
                skPath.Dispose();
                return;
            }

            // Create shader with the effective matrix (device coords → tile coords)
            using var shader = SKShader.CreateBitmap(
                tileBitmap,
                SKShaderTileMode.Repeat,
                SKShaderTileMode.Repeat,
                effectiveShaderMatrix);

            if (shader is null)
            {
                PdfLogger.Log(LogCategory.Graphics, "PATTERN SHADER: Failed to create shader");
                skPath.Dispose();
                return;
            }

            // Fill the path with the pattern shader
            using var paint = new SKPaint();
            paint.Shader = shader;
            paint.IsAntialias = true;
            paint.Style = SKPaintStyle.Fill;

            // Apply fill alpha
            paint.Color = ColorConverter.ApplyAlpha(SKColors.White, state.FillAlpha);

            _canvas.DrawPath(skPath, paint);
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

    /// <summary>
    /// Convert PDF blend mode string to SkiaSharp blend mode
    /// </summary>
    private SKBlendMode ConvertBlendMode(string blendMode)
    {
        return blendMode switch
        {
            "Normal" or "Compatible" => SKBlendMode.SrcOver,
            "Multiply" => SKBlendMode.Multiply,
            "Screen" => SKBlendMode.Screen,
            "Overlay" => SKBlendMode.Overlay,
            "Darken" => SKBlendMode.Darken,
            "Lighten" => SKBlendMode.Lighten,
            "ColorDodge" => SKBlendMode.ColorDodge,
            "ColorBurn" => SKBlendMode.ColorBurn,
            "HardLight" => SKBlendMode.HardLight,
            "SoftLight" => SKBlendMode.SoftLight,
            "Difference" => SKBlendMode.Difference,
            "Exclusion" => SKBlendMode.Exclusion,
            "Hue" => SKBlendMode.Hue,
            "Saturation" => SKBlendMode.Saturation,
            "Color" => SKBlendMode.Color,
            "Luminosity" => SKBlendMode.Luminosity,
            _ => SKBlendMode.SrcOver
        };
    }
}
