using System.Numerics;
using Logging;
using PdfLibrary.Content;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Rendering.SkiaSharp.Rendering;
using PdfLibrary.Rendering.SkiaSharp.State;
using PdfLibrary.Structure;
using SkiaSharp;

namespace PdfLibrary.Rendering.SkiaSharp;

/// <summary>
/// SkiaSharp-based render target for pixel-perfect PDF rendering.
/// Uses embedded font glyph outlines for text rendering.
/// </summary>
public class SkiaSharpRenderTarget : IRenderTarget, IDisposable
{
    private readonly SKCanvas _canvas;
    private readonly SKSurface _surface;
    private readonly CanvasStateManager _stateManager;
    private readonly SoftMaskManager _softMaskManager;
    private readonly PathRenderer _pathRenderer;
    private readonly TextRenderer _textRenderer;
    private readonly ImageRenderer _imageRenderer;
    private readonly PdfDocument? _document;
    private double _pageWidth;
    private double _pageHeight;
    private double _scale = 1.0;
    private Matrix3x2 _initialTransform = Matrix3x2.Identity;

    // Background color for the canvas (white for normal pages, transparent for mask rendering)
    private readonly SKColor _backgroundColor;

    // Track the last document to detect when a new document is loaded
    // WeakReference prevents keeping old documents alive in memory
    private static WeakReference<PdfDocument>? _lastDocument;
    private static readonly Lock DocumentLock = new();

    public int CurrentPageNumber { get; private set; }

    // Track if we need transparent output (for masks) vs. transparent rendering with white composite (for blend modes)
    private readonly bool _transparentOutput;

    public SkiaSharpRenderTarget(int width, int height, PdfDocument? document = null, bool transparentBackground = false)
    {
        _document = document;
        _transparentOutput = transparentBackground;
        // ALWAYS use transparent during rendering for correct blend mode behavior
        // We'll composite onto white in SaveToFile (unless transparentOutput is true)
        _backgroundColor = SKColors.Transparent;

        // Clear glyph cache when a new document is loaded to prevent
        // cached glyphs from one PDF's subset fonts being reused for another
        if (document is not null)
        {
            lock (DocumentLock)
            {
                var isNewDocument = true;
                if (_lastDocument is not null && _lastDocument.TryGetTarget(out PdfDocument? lastDoc))
                {
                    isNewDocument = !ReferenceEquals(lastDoc, document);
                }

                if (isNewDocument)
                {
                    TextRenderer.ClearCache();
                    _lastDocument = new WeakReference<PdfDocument>(document);
                }
            }
        }

        // Create SkiaSharp surface
        var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        _surface = SKSurface.Create(imageInfo);
        _canvas = _surface.Canvas;

        // Initialize state and rendering managers
        _stateManager = new CanvasStateManager(_canvas);
        _softMaskManager = new SoftMaskManager(_canvas, () => ((float)_pageWidth * (float)_scale, (float)_pageHeight * (float)_scale));
        _pathRenderer = new PathRenderer(_canvas, () => _initialTransform, _surface, _scale);
        _textRenderer = new TextRenderer(_canvas);
        _imageRenderer = new ImageRenderer(_canvas, document);

        // Clear to the background color
        _canvas.Clear(_backgroundColor);
    }

    // ==================== PAGE LIFECYCLE ====================

    public void BeginPage(int pageNumber, double width, double height, double scale = 1.0, double cropOffsetX = 0, double cropOffsetY = 0, int rotation = 0)
    {
        CurrentPageNumber = pageNumber;
        // Store the final page dimensions (after rotation, unscaled) for coordinate calculations
        // For 90°/270° rotation, dimensions swap
        if (rotation == 90 || rotation == 270)
        {
            _pageWidth = height;  // Swapped
            _pageHeight = width;  // Swapped
        }
        else
        {
            _pageWidth = width;
            _pageHeight = height;
        }
        _scale = scale;

        PdfLogger.Log(LogCategory.Transforms, $"BeginPage: Page {pageNumber}, Size: {width}x{height}, Scale: {scale:F2}, CropOffset: ({cropOffsetX}, {cropOffsetY}), Rotation: {rotation}°");

        // Clear canvas for the new page (use configured background color)
        _canvas.Clear(_backgroundColor);

        // Set up initial viewport transformation:
        // PDF coordinate system has origin at bottom-left with Y increasing upward
        // The screen coordinate system has origin at top-left with Y increasing downward
        // So we need to flip the Y-axis AND apply the scale factor
        //
        // When CropBox has an offset (e.g., CropBox [42, 60, 559, 719] vs MediaBox [0, 0, 612, 792]):
        // - Content at PDF coordinate (42, 60) should render at output pixel (0, height*scale) (bottom-left of output)
        // - We need to translate by (-cropOffsetX, -cropOffsetY) in PDF space BEFORE the Y-flip and scale
        //
        // IMPORTANT: width and height parameters are UNROTATED CropBox dimensions
        // (PdfRenderer passes cropBox.Width and cropBox.Height which are not rotation-adjusted)
        //
        // Transform order (applied right to left):
        // 1. Translate by (-cropOffsetX, -cropOffsetY) - shift PDF coordinates so CropBox origin is at (0,0)
        // 2. Apply rotation if needed (around center of unrotated page)
        // 3. Scale by (scale, -scale) - scales content and flips Y
        // 4. Translate by (0, finalHeight * scale) - moves origin to top-left of scaled output

        Matrix3x2 initialTransform;

        if (rotation == 0)
        {
            // No rotation - standard transform
            initialTransform = Matrix3x2.CreateTranslation((float)-cropOffsetX, (float)-cropOffsetY)
                             * Matrix3x2.CreateScale((float)scale, (float)-scale)
                             * Matrix3x2.CreateTranslation(0, (float)(height * scale));
        }
        else
        {
            // Rotation is specified in degrees clockwise (0, 90, 180, or 270)
            // Matrix3x2.CreateRotation() expects radians counterclockwise, so negate for clockwise
            float rotationRadians = -(float)(rotation * Math.PI / 180.0);

            // Calculate dimensions after rotation (swap for 90°/270°)
            float finalWidth = (rotation == 90 || rotation == 270) ? (float)height : (float)width;
            float finalHeight = (rotation == 90 || rotation == 270) ? (float)width : (float)height;

            // Rotate around origin (0,0) in PDF space, then translate to position correctly
            // After rotation, the page bounding box may be in negative coordinate space,
            // so we need to translate it back to positive space.
            //
            // For each rotation angle, after rotating around origin:
            // - 90° clockwise: translate by (0, width)
            // - 180°: translate by (width, height)
            // - 270° clockwise: translate by (height, 0)

            float translateX = 0, translateY = 0;
            switch (rotation)
            {
                case 90:
                    translateX = 0;
                    translateY = (float)width;
                    break;
                case 180:
                    translateX = (float)width;
                    translateY = (float)height;
                    break;
                case 270:
                    translateX = (float)height;
                    translateY = 0;
                    break;
            }

            // Transform order (right to left):
            // 1. Remove crop offset in PDF space
            // 2. Rotate around origin
            // 3. Translate to move rotated content to positive coordinates
            // 4. Scale and flip Y-axis
            // 5. Position on canvas
            initialTransform = Matrix3x2.CreateTranslation((float)-cropOffsetX, (float)-cropOffsetY)
                             * Matrix3x2.CreateRotation(rotationRadians)
                             * Matrix3x2.CreateTranslation(translateX, translateY)
                             * Matrix3x2.CreateScale((float)scale, (float)-scale)
                             * Matrix3x2.CreateTranslation(0, (float)(finalHeight * scale));
        }

        // Store this as our base transformation
        // All PDF CTM transformations will be applied on top of this
        _initialTransform = initialTransform;

        // Set the canvas to the initial transform
        var initialMatrix = new SKMatrix(
            initialTransform.M11, initialTransform.M21, initialTransform.M31,
            initialTransform.M12, initialTransform.M22, initialTransform.M32,
            0, 0, 1
        );
        _canvas.SetMatrix(initialMatrix);
    }

    public void EndPage()
    {
        _canvas.Flush();
    }

    public void Clear()
    {
        _canvas.Clear(SKColors.White);
        _stateManager.Clear();
        CurrentPageNumber = 0;
        _softMaskManager.Clear();
    }

    // ==================== STATE MANAGEMENT ====================

    public void SaveState()
    {
        _stateManager.Save();
    }

    public void RestoreState()
    {
        // Let soft mask manager handle mask cleanup if this state owns a mask
        _softMaskManager.OnBeforeRestore(_stateManager.CurrentDepth);

        // Restore the canvas state
        _stateManager.Restore();
    }

    public void ApplyCtm(Matrix3x2 ctm)
    {
        // The CTM parameter is the FULL accumulated transformation matrix from PdfGraphicsState.
        // We need to combine it with our initial viewport transformation.
        //
        // Matrix multiplication order: In System.Numerics.Matrix3x2, A * B means "apply A first, then B".
        // A point p transforms as: p' = p * (A * B) which equals (p * A) * B
        // We want: CTM applied first (position/scale in user space), then InitialTransform (user→device).
        // So: CTM × InitialTransform

        // Combine: point goes through CTM first, then InitialTransform converts to device coords
        Matrix3x2 finalTransform = ctm * _initialTransform;

        // Convert to SKMatrix
        // Matrix3x2 to SKMatrix mapping: M11→scaleX, M21→skewX, M31→transX, M12→skewY, M22→scaleY, M32→transY
        var finalMatrix = new SKMatrix(
            finalTransform.M11, finalTransform.M21, finalTransform.M31,  // scaleX, skewX, transX
            finalTransform.M12, finalTransform.M22, finalTransform.M32,  // skewY, scaleY, transY
            0, 0, 1
        );

        _canvas.SetMatrix(finalMatrix);
    }

    public void OnGraphicsStateChanged(PdfGraphicsState state)
    {
        // This is called when the gs operator changes ExtGState parameters.
        // The actual alpha/blend mode will be applied in the drawing methods.
        if (state.SoftMask is null)
        {
            // SMask cleared - clear any active soft mask
            ClearSoftMask();
        }
    }

    /// <summary>
    /// Sets the active soft mask from a pre-rendered bitmap.
    /// The bitmap should contain alpha values where 255 = fully opaque, 0 = fully transparent.
    /// Called by PdfRenderer after rendering the SMask's Group XObject.
    /// </summary>
    /// <param name="maskBitmap">The rendered soft mask bitmap (grayscale or RGBA with alpha)</param>
    /// <param name="subtype">The mask subtype: "Alpha" or "Luminosity"</param>
    public void SetSoftMask(SKBitmap maskBitmap, string subtype)
    {
        _softMaskManager.SetMask(maskBitmap, subtype, _stateManager.CurrentDepth);
    }

    public void ClearSoftMask()
    {
        _softMaskManager.Clear();
    }

    /// <summary>
    /// Gets the current page dimensions for creating offscreen surfaces.
    /// </summary>
    public (int width, int height, double scale) GetPageDimensions()
    {
        return ((int)Math.Ceiling(_pageWidth * _scale), (int)Math.Ceiling(_pageHeight * _scale), _scale);
    }

    /// <summary>
    /// Renders a soft mask using the provided content renderer callback.
    /// Creates an offscreen surface, calls the callback to render mask content,
    /// and applies the resulting mask for subsequent drawing operations.
    /// </summary>
    /// <param name="maskSubtype">The mask subtype: "Alpha" or "Luminosity"</param>
    /// <param name="renderMaskContent">Callback that renders the mask content to the provided render target</param>
    public void RenderSoftMask(string maskSubtype, Action<IRenderTarget> renderMaskContent)
    {
        try
        {
            (int width, int height, double scale) = GetPageDimensions();

            PdfLogger.Log(LogCategory.Graphics, $"RenderSoftMask: Rendering mask to {width}x{height} surface");

            // Create a temporary render target for the mask with TRANSPARENT background
            // This is critical for Alpha-type soft masks: the alpha channel of the rendered
            // content determines what is visible. Background must be transparent (alpha=0)
            // so unpainted areas allow no content through.
            using var maskRenderTarget = new SkiaSharpRenderTarget(width, height, _document, transparentBackground: true);
            maskRenderTarget.BeginPage(1, _pageWidth, _pageHeight, scale);

            // Call the render callback to render the mask content
            renderMaskContent(maskRenderTarget);

            maskRenderTarget.EndPage();

            // Get the rendered mask image
            using SKImage maskImage = maskRenderTarget.GetImage();
            SKBitmap maskBitmap = SKBitmap.FromImage(maskImage);

            // Convert to alpha mask based on subtype
            SKBitmap alphaMask;
            if (maskSubtype == "Luminosity")
            {
                // Luminosity mask: convert RGB luminosity to alpha
                alphaMask = SoftMaskManager.ConvertToLuminosityMask(maskBitmap);
                maskBitmap.Dispose();
            }
            else
            {
                // Alpha mask: use the alpha channel directly
                alphaMask = maskBitmap;
            }

            // Set the soft mask for subsequent rendering
            SetSoftMask(alphaMask, maskSubtype);

            PdfLogger.Log(LogCategory.Graphics, "RenderSoftMask: Mask rendered successfully");
        }
        catch (Exception ex)
        {
            PdfLogger.Log(LogCategory.Graphics, $"RenderSoftMask: Error rendering mask: {ex.Message}");
        }
    }

    // ==================== TEXT RENDERING ====================

    public void DrawText(string text, List<double> glyphWidths, PdfGraphicsState state, PdfFont? font, List<int>? charCodes = null)
    {
        _textRenderer.DrawText(text, glyphWidths, state, font, charCodes);
    }

    // ==================== PATH OPERATIONS ====================

    public void StrokePath(IPathBuilder path, PdfGraphicsState state)
    {
        _pathRenderer.StrokePath(path, state);
    }

    public void FillPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        _pathRenderer.FillPath(path, state, evenOdd);
    }

    public void FillPathWithTilingPattern(IPathBuilder path, PdfGraphicsState state, bool evenOdd,
        PdfTilingPattern pattern, Action<IRenderTarget> renderPatternContent)
    {
        _pathRenderer.FillPathWithTilingPattern(path, state, evenOdd, pattern, renderPatternContent);
    }

    public void FillAndStrokePath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        _pathRenderer.FillAndStrokePath(path, state, evenOdd);
    }

    public void SetClippingPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        _pathRenderer.SetClippingPath(path, state, evenOdd);
    }


    public float MeasureTextWidth(string text, PdfGraphicsState state, PdfFont font)
    {
        return _textRenderer.MeasureTextWidth(text, state, font);
    }

    public void DrawImage(PdfImage image, PdfGraphicsState state)
    {
        _imageRenderer.DrawImage(image, state);
    }

    // ==================== PUBLIC API ====================

    /// <summary>
    /// Get the rendered image as an SKImage
    /// </summary>
    public SKImage GetImage()
    {
        return _surface.Snapshot();
    }

    /// <summary>
    /// Save the rendered image to a file
    /// </summary>
    public void SaveToFile(string filePath, SKEncodedImageFormat format = SKEncodedImageFormat.Png, int quality = 100)
    {
        SKImage imageToSave;

        if (_transparentOutput)
        {
            // For masks/soft masks, save the transparent result directly
            imageToSave = _surface.Snapshot();
        }
        else
        {
            // For normal page rendering, composite the transparent result onto white
            // This is required for correct blend mode rendering
            SKRectI imageInfo = _surface.Canvas.DeviceClipBounds;
            var finalInfo = new SKImageInfo(imageInfo.Width, imageInfo.Height, SKColorType.Rgba8888, SKAlphaType.Premul);

            using var finalSurface = SKSurface.Create(finalInfo);
            SKCanvas? finalCanvas = finalSurface.Canvas;
            finalCanvas.Clear(SKColors.White);

            // Composite the transparent result onto white
            using SKImage? transparentSnapshot = _surface.Snapshot();
            finalCanvas.DrawImage(transparentSnapshot, 0, 0);

            imageToSave = finalSurface.Snapshot();
        }

        using (imageToSave)
        using (SKData? data = imageToSave.Encode(format, quality))
        using (FileStream stream = File.OpenWrite(filePath))
        {
            data.SaveTo(stream);
        }
    }

    public void Dispose()
    {
        Clear(); // Clean up soft masks
        _surface.Dispose();
    }
}