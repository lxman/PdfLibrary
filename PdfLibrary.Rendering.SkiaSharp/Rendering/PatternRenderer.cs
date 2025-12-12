using System.Numerics;
using Logging;
using PdfLibrary.Content;
using PdfLibrary.Rendering.SkiaSharp.Conversion;
using SkiaSharp;

namespace PdfLibrary.Rendering.SkiaSharp.Rendering;

/// <summary>
/// Handles tiling pattern rendering operations for PDF content.
/// Responsible for creating pattern tiles, setting up shaders, and filling paths with patterns.
/// </summary>
internal class PatternRenderer
{
    private readonly SKCanvas _canvas;
    private readonly Func<Matrix3x2> _getInitialTransform;
    private readonly double _scale;

    /// <summary>
    /// Creates a new PatternRenderer.
    /// </summary>
    /// <param name="canvas">The SKCanvas to draw on</param>
    /// <param name="getInitialTransform">Function that returns the current initial transformation matrix for display (Y-flip + crop offset)</param>
    /// <param name="scale">Rendering scale factor</param>
    public PatternRenderer(SKCanvas canvas, Func<Matrix3x2> getInitialTransform, double scale)
    {
        _canvas = canvas;
        _getInitialTransform = getInitialTransform;
        _scale = scale;
    }

    /// <summary>
    /// Fills a path with a tiling pattern.
    /// </summary>
    public void FillPathWithTilingPattern(
        SKPath skPath,
        PdfGraphicsState state,
        PdfTilingPattern pattern,
        Action<IRenderTarget> renderPatternContent)
    {
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
            return;
        }

        // Create a shader that tiles the pattern image
        SKShader? shader = CreatePatternShader(
            tileImage,
            pattern,
            patternWidth,
            patternHeight,
            tileWidth,
            tileHeight);

        if (shader is null)
        {
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
        shader.Dispose();
    }

    /// <summary>
    /// Creates a shader for tiling pattern rendering.
    /// The shader maps from device space coordinates to tile bitmap coordinates.
    /// </summary>
    private SKShader? CreatePatternShader(
        SKImage tileImage,
        PdfTilingPattern pattern,
        double patternWidth,
        double patternHeight,
        int tileWidth,
        int tileHeight)
    {
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
            return null;
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
            return null;
        }

        // Create shader with the effective matrix (device coords → tile coords)
        var shader = SKShader.CreateBitmap(
            tileBitmap,
            SKShaderTileMode.Repeat,
            SKShaderTileMode.Repeat,
            effectiveShaderMatrix);

        if (shader is null)
        {
            PdfLogger.Log(LogCategory.Graphics, "PATTERN SHADER: Failed to create shader");
            return null;
        }

        return shader;
    }
}
