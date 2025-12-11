using System.Numerics;
using System.Runtime.InteropServices;
using Compressors.Jpeg2000;
using Logging;
using Microsoft.Extensions.Caching.Memory;
using PdfLibrary.Content;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Structure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;
using PdfLibrary.Rendering.SkiaSharp.Conversion;
using PdfLibrary.Rendering.SkiaSharp.State;
using PdfLibrary.Rendering.SkiaSharp.Rendering;

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
    private static readonly object _documentLock = new();

    public int CurrentPageNumber { get; private set; }

    public SkiaSharpRenderTarget(int width, int height, PdfDocument? document = null, bool transparentBackground = false)
    {
        _document = document;
        _backgroundColor = transparentBackground ? SKColors.Transparent : SKColors.White;

        // Clear glyph cache when a new document is loaded to prevent
        // cached glyphs from one PDF's subset fonts being reused for another
        if (document is not null)
        {
            lock (_documentLock)
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
            if (rotation == 90)
            {
                translateX = 0;
                translateY = (float)width;
            }
            else if (rotation == 180)
            {
                translateX = (float)width;
                translateY = (float)height;
            }
            else if (rotation == 270)
            {
                translateX = (float)height;
                translateY = 0;
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
    /// Gets the initial transform matrix for creating offscreen surfaces.
    /// </summary>
    public Matrix3x2 GetInitialTransform()
    {
        return _initialTransform;
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
                alphaMask = ConvertToLuminosityMask(maskBitmap);
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

    /// <summary>
    /// Converts a rendered bitmap to a luminosity-based alpha mask.
    /// The luminosity (grayscale value) of each pixel becomes the alpha value.
    /// </summary>
    private static SKBitmap ConvertToLuminosityMask(SKBitmap source)
    {
        var result = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                SKColor pixel = source.GetPixel(x, y);
                // Calculate luminosity: 0.299*R + 0.587*G + 0.114*B
                var luminosity = (byte)(0.299 * pixel.Red + 0.587 * pixel.Green + 0.114 * pixel.Blue);
                // Use luminosity as alpha, with white (255, 255, 255) for the color
                result.SetPixel(x, y, new SKColor(255, 255, 255, luminosity));
            }
        }

        return result;
    }

    /// <summary>
    /// Converts ImageSharp Image to SKBitmap (supports RGB24, Rgba32, L8)
    /// </summary>
    private static SKBitmap ConvertImageSharpToSkBitmap(Image image)
    {
        switch (image)
        {
            case Image<Rgb24> rgb24:
            {
                var bitmap = new SKBitmap(rgb24.Width, rgb24.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                int width = rgb24.Width;
                int height = rgb24.Height;
                var pixelBuffer = new byte[width * height * 4]; // RGBA8888

                // Copy pixels using direct memory access
                for (var y = 0; y < height; y++)
                {
                    Span<Rgb24> rowSpan = rgb24.DangerousGetPixelRowMemory(y).Span;
                    int bufferOffset = y * width * 4;

                    for (var x = 0; x < width; x++)
                    {
                        Rgb24 pixel = rowSpan[x];
                        int offset = bufferOffset + (x * 4);
                        pixelBuffer[offset] = pixel.R;
                        pixelBuffer[offset + 1] = pixel.G;
                        pixelBuffer[offset + 2] = pixel.B;
                        pixelBuffer[offset + 3] = 255; // Alpha
                    }
                }

                // Bulk copy to bitmap
                IntPtr bitmapPixels = bitmap.GetPixels();
                if (bitmapPixels == IntPtr.Zero) return bitmap;
                Marshal.Copy(pixelBuffer, 0, bitmapPixels, pixelBuffer.Length);
                bitmap.NotifyPixelsChanged();

                return bitmap;
            }
            case Image<Rgba32> rgba32:
            {
                var bitmap = new SKBitmap(rgba32.Width, rgba32.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                int width = rgba32.Width;
                int height = rgba32.Height;
                var pixelBuffer = new byte[width * height * 4]; // RGBA8888

                // Copy pixels using direct memory access
                for (var y = 0; y < height; y++)
                {
                    Span<Rgba32> rowSpan = rgba32.DangerousGetPixelRowMemory(y).Span;
                    int bufferOffset = y * width * 4;

                    for (var x = 0; x < width; x++)
                    {
                        Rgba32 pixel = rowSpan[x];
                        int offset = bufferOffset + (x * 4);
                        pixelBuffer[offset] = pixel.R;
                        pixelBuffer[offset + 1] = pixel.G;
                        pixelBuffer[offset + 2] = pixel.B;
                        pixelBuffer[offset + 3] = pixel.A;
                    }
                }

                // Bulk copy to bitmap
                IntPtr bitmapPixels = bitmap.GetPixels();
                if (bitmapPixels == IntPtr.Zero) return bitmap;
                Marshal.Copy(pixelBuffer, 0, bitmapPixels, pixelBuffer.Length);
                bitmap.NotifyPixelsChanged();

                return bitmap;
            }
            case Image<L8> l8:
            {
                var bitmap = new SKBitmap(l8.Width, l8.Height, SKColorType.Gray8, SKAlphaType.Opaque);
                int width = l8.Width;
                int height = l8.Height;
                var pixelBuffer = new byte[width * height]; // Gray8 = 1 byte per pixel

                // Copy pixels using direct memory access
                for (var y = 0; y < height; y++)
                {
                    Span<L8> rowSpan = l8.DangerousGetPixelRowMemory(y).Span;
                    int bufferOffset = y * width;

                    for (var x = 0; x < width; x++)
                    {
                        pixelBuffer[bufferOffset + x] = rowSpan[x].PackedValue;
                    }
                }

                // Bulk copy to bitmap
                IntPtr bitmapPixels = bitmap.GetPixels();
                if (bitmapPixels == IntPtr.Zero) return bitmap;
                Marshal.Copy(pixelBuffer, 0, bitmapPixels, pixelBuffer.Length);
                bitmap.NotifyPixelsChanged();

                return bitmap;
            }
            default:
                throw new NotSupportedException($"ImageSharp pixel format {image.GetType().Name} not supported for conversion to SKBitmap");
        }
    }

    // ==================== TEXT RENDERING ====================

    public void DrawText(string text, List<double> glyphWidths, PdfGraphicsState state, PdfFont? font, List<int>? charCodes = null)
    {
        _textRenderer.DrawText(text, glyphWidths, state, font, charCodes);
    }

    private void ApplyTransformationMatrix(PdfGraphicsState state)
    {
        // Following Melville.Pdf architecture (GraphicsStateHelpers.cs GlyphTransformMatrix):
        // Build glyph transformation matrix = [[Tfs × Th, 0, 0], [0, Tfs, 0], [0, Trise, 1]] × Tm
        // Where:
        //   Tfs = Font Size
        //   Th = Horizontal Text Scale
        //   Trise = Text Rise
        //   Tm = Text Matrix

        var tFs = (float)state.FontSize;
        var tHs = (float)state.HorizontalScaling;
        var tRise = (float)state.TextRise;

        // Create a text state matrix
        var textStateMatrix = new Matrix3x2(
            tFs * tHs, 0,      // Scale X by FontSize × HorizontalScale
            0, tFs,            // Scale Y by FontSize
            0, tRise           // Translate Y by TextRise
        );

        // Multiply by text matrix to get a complete glyph transformation
        Matrix3x2 glyphMatrix = textStateMatrix * state.TextMatrix;

        // Convert to SKMatrix
        var skMatrix = new SKMatrix
        {
            ScaleX = glyphMatrix.M11,
            SkewY = glyphMatrix.M12,
            SkewX = glyphMatrix.M21,
            ScaleY = glyphMatrix.M22,
            TransX = glyphMatrix.M31,
            TransY = glyphMatrix.M32,
            Persp0 = 0,
            Persp1 = 0,
            Persp2 = 1
        };

        // Concatenate with canvas matrix (which already has displayMatrix applied)
        // Note: SKCanvas.Concat does PRE-multiply (new × current), but we need POST-multiply (current × new)
        // So we need to reverse the order to get the correct PDF transformation
        _canvas.Concat(in skMatrix);
    }

    private SKColor ConvertColor(List<double> colorComponents, string colorSpace)
    {
        if (colorComponents.Count == 0)
            return SKColors.Black;

        switch (colorSpace)
        {
            case "DeviceGray":
                {
                    var gray = (byte)(colorComponents[0] * 255);
                    return new SKColor(gray, gray, gray);
                }
            case "DeviceRGB":
                if (colorComponents.Count >= 3)
                {
                    // Clamp to [0, 1] before converting to byte to prevent overflow
                    var r = (byte)(Math.Clamp(colorComponents[0], 0.0, 1.0) * 255);
                    var g = (byte)(Math.Clamp(colorComponents[1], 0.0, 1.0) * 255);
                    var b = (byte)(Math.Clamp(colorComponents[2], 0.0, 1.0) * 255);
                    return new SKColor(r, g, b);
                }
                break;
            case "DeviceCMYK":
                if (colorComponents.Count >= 4)
                {
                    double c = colorComponents[0];
                    double m = colorComponents[1];
                    double y = colorComponents[2];
                    double k = colorComponents[3];

                    // Improved CMYK to RGB conversion
                    // Adobe uses ICC profiles, but this provides a reasonable approximation
                    // The key insight is that we convert to CMY first, then apply black
                    var r = (byte)((1 - Math.Min(1.0, c * (1 - k) + k)) * 255);
                    var g = (byte)((1 - Math.Min(1.0, m * (1 - k) + k)) * 255);
                    var b = (byte)((1 - Math.Min(1.0, y * (1 - k) + k)) * 255);

                    // Debug logging for CMYK conversion
                    PdfLogger.Log(LogCategory.Graphics,
                        $"CMYK→RGB: CMYK=[{c:F2},{m:F2},{y:F2},{k:F2}] → RGB=({r},{g},{b})");

                    return new SKColor(r, g, b);
                }
                break;
            case "Lab":
                if (colorComponents.Count >= 3)
                {
                    // Lab color space: L* (0-100), a* (-128 to 127), b* (-128 to 127)
                    double L = colorComponents[0];
                    double a = colorComponents[1];
                    double b = colorComponents[2];

                    // Default white point (D65 if not specified)
                    double Xn = 0.9642, Yn = 1.0, Zn = 0.8249;

                    // Convert Lab to XYZ
                    double fy = (L + 16) / 116.0;
                    double fx = fy + (a / 500.0);
                    double fz = fy - (b / 200.0);

                    double xr = fx * fx * fx;
                    if (xr <= 0.008856) xr = (fx - 16.0 / 116.0) / 7.787;

                    double yr = fy * fy * fy;
                    if (yr <= 0.008856) yr = (fy - 16.0 / 116.0) / 7.787;

                    double zr = fz * fz * fz;
                    if (zr <= 0.008856) zr = (fz - 16.0 / 116.0) / 7.787;

                    double X = xr * Xn;
                    double Y = yr * Yn;
                    double Z = zr * Zn;

                    // Convert XYZ to sRGB (using standard D65 matrix)
                    double rLinear =  3.2406 * X - 1.5372 * Y - 0.4986 * Z;
                    double gLinear = -0.9689 * X + 1.8758 * Y + 0.0415 * Z;
                    double bLinear =  0.0557 * X - 0.2040 * Y + 1.0570 * Z;

                    // Apply gamma correction for sRGB
                    var gamma = (double v) => v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1.0 / 2.4) - 0.055;
                    double rSrgb = gamma(rLinear);
                    double gSrgb = gamma(gLinear);
                    double bSrgb = gamma(bLinear);

                    // Clamp to [0, 1] and convert to byte
                    var clamp = (double v) => Math.Max(0, Math.Min(1, v));
                    var rByte = (byte)(clamp(rSrgb) * 255);
                    var gByte = (byte)(clamp(gSrgb) * 255);
                    var bByte = (byte)(clamp(bSrgb) * 255);

                    // Debug logging for Lab conversion
                    PdfLogger.Log(LogCategory.Graphics,
                        $"Lab→RGB: Lab=[{L:F2},{a:F2},{b:F2}] → RGB=({rByte},{gByte},{bByte})");

                    return new SKColor(rByte, gByte, bByte);
                }
                break;
            default:
                switch (colorComponents.Count)
                {
                    // For named/unknown color spaces, try to interpret based on component count
                    // This is a fallback - proper implementation would resolve the named color space
                    case >= 4:
                    {
                        // Treat as CMYK
                        double c = colorComponents[0];
                        double m = colorComponents[1];
                        double y = colorComponents[2];
                        double k = colorComponents[3];
                        var r = (byte)((1 - c) * (1 - k) * 255);
                        var g = (byte)((1 - m) * (1 - k) * 255);
                        var b = (byte)((1 - y) * (1 - k) * 255);
                        return new SKColor(r, g, b);
                    }
                    case >= 3:
                    {
                        // Treat as RGB
                        var r = (byte)(colorComponents[0] * 255);
                        var g = (byte)(colorComponents[1] * 255);
                        var b = (byte)(colorComponents[2] * 255);
                        return new SKColor(r, g, b);
                    }
                    case >= 1:
                    {
                        // Treat as grayscale
                        var gray = (byte)(colorComponents[0] * 255);
                        return new SKColor(gray, gray, gray);
                    }
                }

                break;
        }

        return SKColors.Black;
    }

    /// <summary>
    /// Applies alpha value from graphics state to a color.
    /// Alpha is specified in PDF as a value from 0.0 (transparent) to 1.0 (opaque).
    /// </summary>
    private static SKColor ApplyAlpha(SKColor color, double alpha)
    {
        if (alpha >= 1.0)
            return color;

        var alphaByte = (byte)(Math.Clamp(alpha, 0.0, 1.0) * 255);
        return color.WithAlpha(alphaByte);
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
        try
        {
            // Create SKBitmap from PDF image data
            // For image masks, pass the fill color to use as the stencil color (with alpha)
            SKColor? fillColor = null;
            if (image.IsImageMask)
            {
                fillColor = ConvertColor(state.ResolvedFillColor, state.ResolvedFillColorSpace);
                fillColor = ApplyAlpha(fillColor.Value, state.FillAlpha);
            }
            SKBitmap? bitmap = CreateBitmapFromPdfImage(image, fillColor);
            if (bitmap is null)
            {
                PdfLogger.Log(LogCategory.Images, "DrawImage: CreateBitmapFromPdfImage returned null!");
                return;
            }

            try
            {
                SKMatrix oldMatrix = _canvas.TotalMatrix;

                // Debug: log the canvas matrix being used for this image
                PdfLogger.Log(LogCategory.Images, $"DrawImage: Canvas matrix=[{oldMatrix.ScaleX:F2},{oldMatrix.SkewX:F2},{oldMatrix.TransX:F2};{oldMatrix.SkewY:F2},{oldMatrix.ScaleY:F2},{oldMatrix.TransY:F2}]");

                // Debug: Check clip bounds BEFORE any matrix changes
                SKRectI deviceClipBefore = _canvas.DeviceClipBounds;
                PdfLogger.Log(LogCategory.Images, $"DrawImage: DeviceClipBounds BEFORE SetMatrix = ({deviceClipBefore.Left},{deviceClipBefore.Top},{deviceClipBefore.Right},{deviceClipBefore.Bottom})");

                // PDF images are drawn in a unit square from (0,0) to (1,1).
                // The canvas already has the correct transform from ApplyCtm() which includes the Y-flip.
                //
                // However, PDF image data is stored with Y=0 at TOP of the image (like most image formats),
                // while PDF coordinates have Y=0 at BOTTOM. So we need to flip the image vertically
                // within the unit square by applying a flip matrix before drawing.

                // Apply image flip: scale Y by -1 and translate by 1 to flip within unit square
                var imageFlipMatrix = new SKMatrix(1, 0, 0, 0, -1, 1, 0, 0, 1);
                SKMatrix combinedMatrix = oldMatrix.PreConcat(imageFlipMatrix);
                _canvas.SetMatrix(combinedMatrix);

                // Debug: log the combined matrix after flip and where corners map
                SKPoint p00 = combinedMatrix.MapPoint(new SKPoint(0, 0));
                SKPoint p11 = combinedMatrix.MapPoint(new SKPoint(1, 1));
                PdfLogger.Log(LogCategory.Images, $"DrawImage: After flip=[{combinedMatrix.ScaleX:F2},{combinedMatrix.SkewX:F2},{combinedMatrix.TransX:F2};{combinedMatrix.SkewY:F2},{combinedMatrix.ScaleY:F2},{combinedMatrix.TransY:F2}]");
                PdfLogger.Log(LogCategory.Images, $"DrawImage: Unit (0,0) maps to ({p00.X:F2},{p00.Y:F2}), (1,1) maps to ({p11.X:F2},{p11.Y:F2})");

                // Debug: Log bitmap info and sample pixels to verify image data
                PdfLogger.Log(LogCategory.Images, $"DrawImage: Bitmap created {bitmap.Width}x{bitmap.Height}, Info={bitmap.Info.ColorType}");
                if (bitmap.Width > 0 && bitmap.Height > 0)
                {
                    SKColor pixel00 = bitmap.GetPixel(0, 0);
                    int midX = bitmap.Width / 2;
                    int midY = bitmap.Height / 2;
                    SKColor pixelMid = bitmap.GetPixel(midX, midY);
                    PdfLogger.Log(LogCategory.Images, $"DrawImage: pixel(0,0)=({pixel00.Red},{pixel00.Green},{pixel00.Blue},{pixel00.Alpha}), pixel(mid)=({pixelMid.Red},{pixelMid.Green},{pixelMid.Blue},{pixelMid.Alpha})");

                    // DEBUG: Save bitmap before drawing to diagnose dithering
                    if (bitmap.Width == 650 && bitmap.Height == 650)  // Only for this specific test image
                    {
                        try
                        {
                            string debugPath = Path.Combine(Path.GetTempPath(), "debug_bitmap_before_draw.png");
                            using SKImage? debugImage = SKImage.FromBitmap(bitmap);
                            using SKData? debugData = debugImage?.Encode(SKEncodedImageFormat.Png, 100);
                            if (debugData != null)
                            {
                                using FileStream stream = File.OpenWrite(debugPath);
                                debugData.SaveTo(stream);
                                PdfLogger.Log(LogCategory.Images, $"DEBUG: Saved bitmap to {debugPath}");
                            }
                        }
                        catch { /* ignore debug errors */ }
                    }
                }

                // Debug: Log canvas clip bounds
                SKRect clipBounds = _canvas.LocalClipBounds;
                SKRectI deviceClipBounds = _canvas.DeviceClipBounds;
                PdfLogger.Log(LogCategory.Images, $"DrawImage: ClipBounds Local=({clipBounds.Left:F2},{clipBounds.Top:F2},{clipBounds.Right:F2},{clipBounds.Bottom:F2}), Device=({deviceClipBounds.Left},{deviceClipBounds.Top},{deviceClipBounds.Right},{deviceClipBounds.Bottom})");

                // Convert bitmap to SKImage for drawing
                using SKImage? skImage = SKImage.FromBitmap(bitmap);
                if (skImage == null)
                {
                    PdfLogger.Log(LogCategory.Images, "DrawImage: SKImage.FromBitmap returned null!");
                    _canvas.SetMatrix(oldMatrix);
                    return;
                }

                // Draw image into unit square with a tiny expansion to cover sub-pixel gaps
                // Some PDFs have tiled images with fractional pixel gaps due to coordinate rounding.
                // A small expansion (0.001 in unit space) prevents visible seams without affecting quality.
                var sourceRect = new SKRect(0, 0, bitmap.Width, bitmap.Height);
                const float epsilon = 0.002f;  // Small expansion to cover sub-pixel gaps
                var destRect = new SKRect(-epsilon, -epsilon, 1 + epsilon, 1 + epsilon);

                // Use high-quality cubic filtering for downscaling images.
                // CatmullRom (B=0, C=0.5) is commonly used for photo-quality downsampling.
                // Linear filtering can cause dithering artifacts when scaling significantly.
                var sampling = new SKSamplingOptions(new SKCubicResampler(0, 0.5f));

                // Apply fill alpha from graphics state to images
                // PDF uses CA for stroke alpha and ca for fill alpha; images use fill alpha
                if (state.FillAlpha < 1.0)
                {
                    using var paint = new SKPaint();
                    paint.Color = ApplyAlpha(SKColors.White, state.FillAlpha);
                    _canvas.DrawImage(skImage, sourceRect, destRect, sampling, paint);
                }
                else
                {
                    _canvas.DrawImage(skImage, sourceRect, destRect, sampling);
                }

                // Restore original matrix
                _canvas.SetMatrix(oldMatrix);
            }
            finally
            {
                // Dispose bitmap
                bitmap.Dispose();
            }
        }
        catch (Exception ex)
        {
            // Image rendering failed, skip this image
            PdfLogger.Log(LogCategory.Images, $"ERROR: {ex.Message}");
        }
    }

    private SKBitmap? CreateBitmapFromPdfImage(PdfImage image, SKColor? imageMaskColor = null)
    {
        try
        {
            byte[] imageData = image.GetDecodedData();
            int width = image.Width;
            int height = image.Height;
            int bitsPerComponent = image.BitsPerComponent;
            string colorSpace = image.ColorSpace;

            // CRITICAL FIX: CoreJ2K.Skia returns Rgb888x which has a known SkiaSharp rendering bug
            // See: https://github.com/mono/SkiaSharp/issues/2671
            // Workaround: Use Decompress() for raw bytes, manually create Rgba8888 bitmap
            PdfStream stream = image.Stream;
            if (stream.Dictionary.TryGetValue(new PdfName("Filter"), out PdfObject? filterObj))
            {
                // Handle both single filter and array of filters
                List<string> filters = new();
                if (filterObj is PdfName filterName)
                    filters.Add(filterName.Value);
                else if (filterObj is PdfArray filterArray)
                    filters.AddRange(filterArray.OfType<PdfName>().Select(n => n.Value));

                // Check if JPXDecode is present
                if (filters.Contains("JPXDecode"))
                {
                    try
                    {
                        // Get raw JP2 data
                        byte[] rawJp2Data = stream.Data;

                        // TIMING: Measure JPEG2000 decode
                        var decodeStart = DateTime.Now;
                        Image jp2Image = Jpeg2000.DecompressToImage(rawJp2Data);
                        var decodeElapsed = DateTime.Now - decodeStart;
                        PdfLogger.Log(LogCategory.Images, $"[TIMING] JPEG2000 decode took {decodeElapsed.TotalMilliseconds:F0}ms for {width}x{height} image");

                        // TIMING: Measure ImageSharp→SKBitmap conversion
                        var convertStart = DateTime.Now;
                        SKBitmap jp2Bitmap = ConvertImageSharpToSkBitmap(jp2Image);
                        var convertElapsed = DateTime.Now - convertStart;
                        PdfLogger.Log(LogCategory.Images, $"[TIMING] ImageSharp→SKBitmap conversion took {convertElapsed.TotalMilliseconds:F0}ms for {width}x{height} image");

                        jp2Image.Dispose();
                        return jp2Bitmap;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[JP2 MANUAL] Failed to decode manually: {ex.Message}");
                        // Fall through to standard processing if manual decode fails
                    }
                }
            }

            // Check for SMask (alpha channel / transparency)
            byte[]? smaskData = null;

            // Check if image has SMask (soft mask - actual alpha channel)
            bool hasActualSMask = stream.Dictionary.ContainsKey(new PdfName("SMask"));
            // Check if image has Mask (color key masking - different from SMask)
            bool hasMask = stream.Dictionary.ContainsKey(new PdfName("Mask"));

            // Only treat as having alpha if there's an actual SMask stream
            // Mask entries are for color key masking, not soft masks
            bool hasSMask = hasActualSMask;
            if (hasSMask)
            {
                // Extract SMask from the image stream
                if (stream.Dictionary.TryGetValue(new PdfName("SMask"), out PdfObject? smaskObj))
                {
                    // Resolve indirect reference if needed
                    if (smaskObj is PdfIndirectReference smaskRef && _document is not null)
                        smaskObj = _document.ResolveReference(smaskRef);

                    if (smaskObj is PdfStream smaskStream)
                    {
                        var smaskImage = new PdfImage(smaskStream, _document);
                        byte[] rawSmaskData = smaskImage.GetDecodedData();

                        // SMask is always grayscale (1 component per pixel)
                        // However, if DCTDecode was used, it might return RGB data (3 components per pixel)
                        // We need to handle both cases
                        int expectedGrayscaleSize = smaskImage.Width * smaskImage.Height;
                        int expectedRgbSize = expectedGrayscaleSize * 3;

                        if (rawSmaskData.Length == expectedRgbSize)
                        {
                            // DCTDecode returned RGB - extract just the first component (they should all be the same for grayscale)
                            smaskData = new byte[expectedGrayscaleSize];
                            for (var i = 0; i < expectedGrayscaleSize; i++)
                            {
                                // Take just the R channel (R=G=B for grayscale JPEG)
                                smaskData[i] = rawSmaskData[i * 3];
                            }
                        }
                        else
                        {
                            // Already grayscale or other format
                            smaskData = rawSmaskData;
                        }
                    }
                }
            }

            // Determine SkiaSharp color type based on PDF color space
            SKBitmap? bitmap = null;

            // Diagnostic: log actual values for image creation
            double[]? imgDecodeArray = image.DecodeArray;
            string decodeStr = imgDecodeArray != null ? $"[{string.Join(", ", imgDecodeArray)}]" : "null";
            int debugExpectedRgb = width * height * 3;
            PdfLogger.Log(LogCategory.Images, $"CreateBitmapFromPdfImage: ColorSpace='{colorSpace}', BitsPerComponent={bitsPerComponent}, Width={width}, Height={height}, DataLength={imageData.Length}, ExpectedRGB={debugExpectedRgb}, Decode={decodeStr}");

            // Handle image masks (1-bit stencil images)
            if (image.IsImageMask && imageMaskColor.HasValue)
            {
                // Image masks are 1-bit images where painted pixels use the fill color
                // Use Premul alpha for better compositing when the mask is scaled
                bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
                SKColor color = imageMaskColor.Value;

                // Check for Decode array - determines how mask bits map to paint/transparent
                // Default [0 1]: sample 0 → paint, sample 1 → transparent
                // Inverted [1 0]: sample 0 → transparent, sample 1 → paint
                double[]? decodeArray = image.DecodeArray;
                bool invertMask = decodeArray is { Length: >= 2 } && decodeArray[0] > decodeArray[1];

                // Each row is byte-aligned (CCITT output is row-padded, not packed)
                int bytesPerRow = (width + 7) / 8;

                // Use direct pixel buffer for performance - SetPixel is very slow for large images
                var pixelBuffer = new byte[width * height * 4]; // RGBA8888
                byte colorR = color.Red, colorG = color.Green, colorB = color.Blue, colorA = color.Alpha;
                var paintedPixels = 0;

                for (var y = 0; y < height; y++)
                {
                    int rowStart = y * bytesPerRow;
                    int bufferRowStart = y * width * 4;

                    for (var x = 0; x < width; x++)
                    {
                        // Calculate byte and bit position (row-aligned data, MSB first)
                        int byteIndex = rowStart + (x >> 3);  // x / 8
                        int bitOffset = 7 - (x & 7);  // 7 - (x % 8), MSB first within each byte

                        int bufferOffset = bufferRowStart + (x << 2);  // x * 4

                        if (byteIndex >= imageData.Length)
                        {
                            // Transparent pixel (RGBA = 0,0,0,0)
                            pixelBuffer[bufferOffset] = 0;
                            pixelBuffer[bufferOffset + 1] = 0;
                            pixelBuffer[bufferOffset + 2] = 0;
                            pixelBuffer[bufferOffset + 3] = 0;
                            continue;
                        }

                        // Get the mask bit
                        // With CCITT default (BlackIs1=false): 0=black (paint), 1=white (transparent)
                        // With Decode default [0 1]: sample 0 → paint, sample 1 → transparent
                        // So with defaults: bit=0 → paint, bit=1 → transparent
                        bool bitIsSet = ((imageData[byteIndex] >> bitOffset) & 1) == 1;
                        // Default: paint when bit is 0 (not set), i.e., !bitIsSet
                        // With invertMask: paint when bit is 1 (set), i.e., bitIsSet
                        bool paint = invertMask ? bitIsSet : !bitIsSet;

                        if (paint)
                        {
                            pixelBuffer[bufferOffset] = colorR;
                            pixelBuffer[bufferOffset + 1] = colorG;
                            pixelBuffer[bufferOffset + 2] = colorB;
                            pixelBuffer[bufferOffset + 3] = colorA;
                            paintedPixels++;
                        }
                        else
                        {
                            // Transparent pixel (RGBA = 0,0,0,0)
                            pixelBuffer[bufferOffset] = 0;
                            pixelBuffer[bufferOffset + 1] = 0;
                            pixelBuffer[bufferOffset + 2] = 0;
                            pixelBuffer[bufferOffset + 3] = 0;
                        }
                    }
                }

                // Copy pixel buffer to bitmap using Marshal.Copy for performance
                IntPtr bitmapPixels = bitmap.GetPixels();
                if (bitmapPixels != IntPtr.Zero)
                {
                    Marshal.Copy(pixelBuffer, 0, bitmapPixels, pixelBuffer.Length);
                    bitmap.NotifyPixelsChanged();
                }

                return bitmap;
            }

            switch (colorSpace)
            {
                case "Indexed":
                {
                    // Handle indexed color images
                    byte[]? paletteData = image.GetIndexedPalette(out string? baseColorSpace, out int hival);
                    if (paletteData is null || baseColorSpace is null)
                        return null;

                    int componentsPerEntry = baseColorSpace switch
                    {
                        "DeviceRGB" => 3,
                        "DeviceGray" => 1,
                        _ => 3
                    };

                    // Build pixel buffer directly - SKBitmap.SetPixel has issues with certain alpha types
                    SKAlphaType alphaType = hasSMask ? SKAlphaType.Premul : SKAlphaType.Opaque;
                    var pixelBuffer = new byte[width * height * 4]; // RGBA8888

                    // Debug: Log first few pixels
                    int debugPixelCount = Math.Min(10, width * height);

                    // Convert indexed pixels to RGBA
                    for (var y = 0; y < height; y++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            int pixelIndex = y * width + x;
                            if (pixelIndex >= imageData.Length)
                                continue;

                            byte paletteIndex = imageData[pixelIndex];
                            if (paletteIndex > hival)
                                paletteIndex = (byte)hival;

                            int paletteOffset = paletteIndex * componentsPerEntry;
                            int bufferOffset = pixelIndex * 4;

                            byte r, g, b, alpha;
                            if (componentsPerEntry == 3 && paletteOffset + 2 < paletteData.Length)
                            {
                                r = paletteData[paletteOffset];
                                g = paletteData[paletteOffset + 1];
                                b = paletteData[paletteOffset + 2];

                                // Debug: Log first few pixels
                                if (pixelIndex < debugPixelCount)
                                {
                                    PdfLogger.Log(LogCategory.Images, $"INDEXED PIXEL[{pixelIndex}]: index={paletteIndex}, offset={paletteOffset}, RGB=({r}, {g}, {b})");
                                }

                                // Apply SMask alpha channel if present
                                alpha = 255;
                                if (smaskData is not null && pixelIndex < smaskData.Length)
                                {
                                    alpha = smaskData[pixelIndex];
                                    // Premultiply RGB by alpha for Premul alpha type
                                    if (hasSMask && alpha < 255)
                                    {
                                        r = (byte)((r * alpha) / 255);
                                        g = (byte)((g * alpha) / 255);
                                        b = (byte)((b * alpha) / 255);
                                    }
                                }
                            }
                            else if (componentsPerEntry == 1 && paletteOffset < paletteData.Length)
                            {
                                byte gray = paletteData[paletteOffset];

                                // Apply SMask alpha channel if present
                                alpha = 255;
                                if (smaskData is not null && pixelIndex < smaskData.Length)
                                {
                                    alpha = smaskData[pixelIndex];
                                    // Premultiply gray by alpha for Premul alpha type
                                    if (hasSMask && alpha < 255)
                                    {
                                        gray = (byte)((gray * alpha) / 255);
                                    }
                                }

                                r = g = b = gray;
                            }
                            else
                            {
                                r = g = b = 0;
                                alpha = 255;
                            }

                            // RGBA8888 format: R, G, B, A
                            pixelBuffer[bufferOffset] = r;
                            pixelBuffer[bufferOffset + 1] = g;
                            pixelBuffer[bufferOffset + 2] = b;
                            pixelBuffer[bufferOffset + 3] = alpha;
                        }
                    }

                    // Create bitmap that owns its own memory, then copy pixels into it
                    var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, alphaType);
                    bitmap = new SKBitmap(imageInfo);

                    // Get pointer to bitmap's pixel buffer and copy our data into it
                    IntPtr bitmapPixels = bitmap.GetPixels();
                    if (bitmapPixels == IntPtr.Zero)
                        return null;

                    // Copy pixel data directly into bitmap's memory
                    Marshal.Copy(pixelBuffer, 0, bitmapPixels, pixelBuffer.Length);

                    // CRITICAL: Notify SkiaSharp that we modified the pixels externally
                    bitmap.NotifyPixelsChanged();

                    break;
                }
                case "DeviceRGB" or "CalRGB" when bitsPerComponent == 8:
                {
                    SKAlphaType alphaType = hasSMask ? SKAlphaType.Unpremul : SKAlphaType.Opaque;
                    int expectedSize = width * height * 3;
                    if (imageData.Length < expectedSize)
                        return null;

                    // Use direct pixel buffer for performance - SetPixel is very slow for large images
                    var pixelBuffer = new byte[width * height * 4]; // RGBA8888

                    for (var y = 0; y < height; y++)
                    {
                        int rowStart = y * width;

                        for (var x = 0; x < width; x++)
                        {
                            int pixelIndex = rowStart + x;
                            int srcOffset = pixelIndex * 3;
                            int dstOffset = pixelIndex * 4;

                            byte r = imageData[srcOffset];
                            byte g = imageData[srcOffset + 1];
                            byte b = imageData[srcOffset + 2];

                            // Apply SMask alpha channel if present
                            byte alpha = 255;
                            if (smaskData is not null && pixelIndex < smaskData.Length)
                                alpha = smaskData[pixelIndex];

                            pixelBuffer[dstOffset] = r;
                            pixelBuffer[dstOffset + 1] = g;
                            pixelBuffer[dstOffset + 2] = b;
                            pixelBuffer[dstOffset + 3] = alpha;
                        }
                    }

                    // DEBUG: Save pixel buffer directly as image to verify data before bitmap copy
                    if (width == 650 && height == 650)
                    {
                        try
                        {
                            // Sample some pixels to check data integrity
                            Console.WriteLine($"[PIXEL DEBUG] Pixel (0,0): R={pixelBuffer[0]}, G={pixelBuffer[1]}, B={pixelBuffer[2]}, A={pixelBuffer[3]}");
                            Console.WriteLine($"[PIXEL DEBUG] Pixel (649,0): R={pixelBuffer[649*4]}, G={pixelBuffer[649*4+1]}, B={pixelBuffer[649*4+2]}, A={pixelBuffer[649*4+3]}");
                            int midRow = 325 * 650 * 4;
                            Console.WriteLine($"[PIXEL DEBUG] Pixel (0,325): R={pixelBuffer[midRow]}, G={pixelBuffer[midRow+1]}, B={pixelBuffer[midRow+2]}, A={pixelBuffer[midRow+3]}");
                            // Check pixel at x=433 where corruption visually starts
                            int corrupt433 = 433 * 4;
                            Console.WriteLine($"[PIXEL DEBUG] Pixel (433,0): R={pixelBuffer[corrupt433]}, G={pixelBuffer[corrupt433+1]}, B={pixelBuffer[corrupt433+2]}, A={pixelBuffer[corrupt433+3]}");
                            int corrupt433Row100 = (100 * 650 + 433) * 4;
                            Console.WriteLine($"[PIXEL DEBUG] Pixel (433,100): R={pixelBuffer[corrupt433Row100]}, G={pixelBuffer[corrupt433Row100+1]}, B={pixelBuffer[corrupt433Row100+2]}, A={pixelBuffer[corrupt433Row100+3]}");

                            // Save the pixel buffer directly using SKBitmap.InstallPixels to bypass Marshal.Copy
                            string debugPath2 = Path.Combine(Path.GetTempPath(), "debug_pixelbuffer_direct.png");
                            var debugInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
                            using var debugBitmap = new SKBitmap();
                            GCHandle pinnedArray = GCHandle.Alloc(pixelBuffer, GCHandleType.Pinned);
                            try
                            {
                                debugBitmap.InstallPixels(debugInfo, pinnedArray.AddrOfPinnedObject(), width * 4);
                                using SKImage? debugImage = SKImage.FromBitmap(debugBitmap);
                                using SKData? debugData = debugImage?.Encode(SKEncodedImageFormat.Png, 100);
                                if (debugData != null)
                                {
                                    using FileStream fileStream = File.OpenWrite(debugPath2);
                                    debugData.SaveTo(fileStream);
                                    Console.WriteLine($"[DEBUG] Saved pixelbuffer direct to {debugPath2}");
                                }
                            }
                            finally
                            {
                                pinnedArray.Free();
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[DEBUG ERROR] {ex.Message}");
                        }
                    }

                    // Create bitmap and copy pixel buffer
                    var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, alphaType);
                    bitmap = new SKBitmap(imageInfo);
                    IntPtr bitmapPixels = bitmap.GetPixels();
                    if (bitmapPixels == IntPtr.Zero)
                        return null;

                    // DEBUG: Check if RowBytes matches our expectation
                    int expectedRowBytes = width * 4;

                    // If RowBytes doesn't match, we need to copy row by row
                    if (bitmap.RowBytes != expectedRowBytes)
                    {
                        // Copy row by row to handle row padding
                        for (var row = 0; row < height; row++)
                        {
                            int srcOffset = row * expectedRowBytes;
                            IntPtr dstOffset = bitmapPixels + (row * bitmap.RowBytes);
                            Marshal.Copy(pixelBuffer, srcOffset, dstOffset, expectedRowBytes);
                        }
                    }
                    else
                    {
                        Marshal.Copy(pixelBuffer, 0, bitmapPixels, pixelBuffer.Length);
                    }
                    bitmap.NotifyPixelsChanged();

                    break;
                }
                case "DeviceGray" or "CalGray" when bitsPerComponent == 1:
                {
                    // 1-bit grayscale image (not an image mask) - commonly from JBIG2Decode or CCITTFaxDecode
                    // Each bit represents a pixel: 0=white (255), 1=black (0)
                    var alphaType = SKAlphaType.Opaque;
                    int bytesPerRow = (width + 7) / 8;
                    int expectedSize = bytesPerRow * height;

                    // Use direct pixel buffer for performance
                    var pixelBuffer1Bit = new byte[width * height * 4]; // RGBA8888

                    for (var y = 0; y < height; y++)
                    {
                        int rowStart = y * bytesPerRow;
                        int bufferRowStart = y * width * 4;

                        for (var x = 0; x < width; x++)
                        {
                            int byteIndex = rowStart + (x >> 3);  // x / 8
                            int bitOffset = 7 - (x & 7);  // MSB first within each byte

                            int bufferOffset = bufferRowStart + (x << 2);  // x * 4

                            // Default to white if data is incomplete
                            byte gray = 255;
                            if (byteIndex < imageData.Length)
                            {
                                // After decoding (JBIG2/CCITT filters already invert to PDF convention):
                                // 0=black, 1=white - this matches PDF DeviceGray sample-to-color mapping
                                bool bitIsSet = ((imageData[byteIndex] >> bitOffset) & 1) == 1;
                                gray = bitIsSet ? (byte)255 : (byte)0;  // 1=white, 0=black
                            }

                            // RGBA8888 format: R, G, B, A
                            pixelBuffer1Bit[bufferOffset] = gray;
                            pixelBuffer1Bit[bufferOffset + 1] = gray;
                            pixelBuffer1Bit[bufferOffset + 2] = gray;
                            pixelBuffer1Bit[bufferOffset + 3] = 255;
                        }
                    }

                    // Create bitmap and copy pixel buffer
                    var imageInfo1Bit = new SKImageInfo(width, height, SKColorType.Rgba8888, alphaType);
                    bitmap = new SKBitmap(imageInfo1Bit);
                    IntPtr bitmapPixels1Bit = bitmap.GetPixels();
                    if (bitmapPixels1Bit == IntPtr.Zero)
                        return null;
                    Marshal.Copy(pixelBuffer1Bit, 0, bitmapPixels1Bit, pixelBuffer1Bit.Length);
                    bitmap.NotifyPixelsChanged();

                    break;
                }
                case "DeviceGray" or "CalGray" when bitsPerComponent == 8:
                {
                    SKAlphaType alphaType = hasSMask ? SKAlphaType.Unpremul : SKAlphaType.Opaque;
                    int expectedSize = width * height;
                    int expectedRgbSize = expectedSize * 3;

                    // Handle DCTDecode (JPEG) returning RGB data for grayscale images
                    // The DctDecodeFilter always decodes to RGB, so we need to extract one channel
                    if (imageData.Length == expectedRgbSize)
                    {
                        var grayData = new byte[expectedSize];
                        for (var i = 0; i < expectedSize; i++)
                        {
                            // For grayscale JPEG, R=G=B, so just take the first channel
                            grayData[i] = imageData[i * 3];
                        }
                        imageData = grayData;
                    }

                    if (imageData.Length < expectedSize)
                        return null;

                    // Use direct pixel buffer for performance - SetPixel is very slow for large images
                    var pixelBuffer = new byte[width * height * 4]; // RGBA8888

                    for (var y = 0; y < height; y++)
                    {
                        int rowStart = y * width;
                        int bufferRowStart = rowStart * 4;

                        for (var x = 0; x < width; x++)
                        {
                            int pixelIndex = rowStart + x;
                            int bufferOffset = bufferRowStart + (x << 2);  // x * 4
                            byte gray = imageData[pixelIndex];

                            // Apply SMask alpha channel if present
                            byte alpha = 255;
                            if (smaskData is not null && pixelIndex < smaskData.Length)
                                alpha = smaskData[pixelIndex];

                            pixelBuffer[bufferOffset] = gray;
                            pixelBuffer[bufferOffset + 1] = gray;
                            pixelBuffer[bufferOffset + 2] = gray;
                            pixelBuffer[bufferOffset + 3] = alpha;
                        }
                    }

                    // Create a bitmap and copy pixel buffer
                    var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, alphaType);
                    bitmap = new SKBitmap(imageInfo);
                    IntPtr bitmapPixels = bitmap.GetPixels();
                    if (bitmapPixels == IntPtr.Zero)
                        return null;
                    Marshal.Copy(pixelBuffer, 0, bitmapPixels, pixelBuffer.Length);
                    bitmap.NotifyPixelsChanged();

                    break;
                }
                case "ICCBased" when bitsPerComponent == 8:
                {
                    // ICCBased images: determine component count from the ICC profile stream
                    int numComponents = GetIccBasedComponentCount(image);

                    switch (numComponents)
                    {
                        case 3:
                        {
                            // Treat as RGB
                            SKAlphaType alphaType = hasSMask ? SKAlphaType.Unpremul : SKAlphaType.Opaque;
                            int expectedSize = width * height * 3;
                            if (imageData.Length < expectedSize)
                                return null;

                            // Use direct pixel buffer for performance
                            var pixelBuffer = new byte[width * height * 4];
                            int pixelCount = width * height;
                            for (var i = 0; i < pixelCount; i++)
                            {
                                int srcOffset = i * 3;
                                int dstOffset = i * 4;
                                pixelBuffer[dstOffset] = imageData[srcOffset];
                                pixelBuffer[dstOffset + 1] = imageData[srcOffset + 1];
                                pixelBuffer[dstOffset + 2] = imageData[srcOffset + 2];
                                pixelBuffer[dstOffset + 3] = (smaskData is not null && i < smaskData.Length) ? smaskData[i] : (byte)255;
                            }

                            var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, alphaType);
                            bitmap = new SKBitmap(imageInfo);
                            IntPtr bitmapPixels = bitmap.GetPixels();
                            if (bitmapPixels == IntPtr.Zero) return null;
                            Marshal.Copy(pixelBuffer, 0, bitmapPixels, pixelBuffer.Length);
                            bitmap.NotifyPixelsChanged();
                            break;
                        }
                        case 1:
                        {
                            // Treat as grayscale
                            SKAlphaType alphaType = hasSMask ? SKAlphaType.Unpremul : SKAlphaType.Opaque;
                            int expectedSize = width * height;
                            if (imageData.Length < expectedSize)
                                return null;

                            // Use direct pixel buffer for performance
                            var pixelBuffer = new byte[width * height * 4];
                            int pixelCount = width * height;
                            for (var i = 0; i < pixelCount; i++)
                            {
                                byte gray = imageData[i];
                                int dstOffset = i * 4;
                                pixelBuffer[dstOffset] = gray;
                                pixelBuffer[dstOffset + 1] = gray;
                                pixelBuffer[dstOffset + 2] = gray;
                                pixelBuffer[dstOffset + 3] = (smaskData is not null && i < smaskData.Length) ? smaskData[i] : (byte)255;
                            }

                            var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, alphaType);
                            bitmap = new SKBitmap(imageInfo);
                            IntPtr bitmapPixels = bitmap.GetPixels();
                            if (bitmapPixels == IntPtr.Zero) return null;
                            Marshal.Copy(pixelBuffer, 0, bitmapPixels, pixelBuffer.Length);
                            bitmap.NotifyPixelsChanged();
                            break;
                        }
                        case 4:
                        {
                            // Treat as CMYK - convert to RGB
                            SKAlphaType alphaType = hasSMask ? SKAlphaType.Unpremul : SKAlphaType.Opaque;
                            int expectedSize = width * height * 4;
                            if (imageData.Length < expectedSize)
                                return null;

                            // Use direct pixel buffer for performance
                            var pixelBuffer = new byte[width * height * 4];
                            int pixelCount = width * height;
                            for (var i = 0; i < pixelCount; i++)
                            {
                                int srcOffset = i * 4;
                                int dstOffset = i * 4;
                                // CMYK values are in range 0-255
                                int c = imageData[srcOffset];
                                int m = imageData[srcOffset + 1];
                                int yy = imageData[srcOffset + 2];
                                int k = imageData[srcOffset + 3];

                                // Convert CMYK to RGB using integer math for performance
                                // r = 255 * (1 - c/255) * (1 - k/255) = (255 - c) * (255 - k) / 255
                                pixelBuffer[dstOffset] = (byte)((255 - c) * (255 - k) / 255);
                                pixelBuffer[dstOffset + 1] = (byte)((255 - m) * (255 - k) / 255);
                                pixelBuffer[dstOffset + 2] = (byte)((255 - yy) * (255 - k) / 255);
                                pixelBuffer[dstOffset + 3] = (smaskData is not null && i < smaskData.Length) ? smaskData[i] : (byte)255;
                            }

                            var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, alphaType);
                            bitmap = new SKBitmap(imageInfo);
                            IntPtr bitmapPixels = bitmap.GetPixels();
                            if (bitmapPixels == IntPtr.Zero) return null;
                            Marshal.Copy(pixelBuffer, 0, bitmapPixels, pixelBuffer.Length);
                            bitmap.NotifyPixelsChanged();
                            break;
                        }
                        default:
                            // Unknown component count
                            return null;
                    }

                    break;
                }
                case "DeviceCMYK" when bitsPerComponent == 8:
                {
                    // CMYK images - convert to RGB
                    // Note: DCTDecode (JPEG) often converts CMYK to RGB automatically during decompression
                    SKAlphaType alphaType = hasSMask ? SKAlphaType.Unpremul : SKAlphaType.Opaque;
                    int expectedCmykSize = width * height * 4;
                    int expectedRgbSize = width * height * 3;
                    int pixelCount = width * height;

                    // Use direct pixel buffer for performance
                    var pixelBuffer = new byte[width * height * 4];

                    if (imageData.Length >= expectedCmykSize)
                    {
                        // True CMYK data - convert to RGB
                        for (var i = 0; i < pixelCount; i++)
                        {
                            int srcOffset = i * 4;
                            int dstOffset = i * 4;
                            // CMYK values are in range 0-255
                            int c = imageData[srcOffset];
                            int m = imageData[srcOffset + 1];
                            int y = imageData[srcOffset + 2];
                            int k = imageData[srcOffset + 3];

                            // Convert CMYK to RGB using integer math for performance
                            // r = 255 * (1 - c/255) * (1 - k/255) = (255 - c) * (255 - k) / 255
                            pixelBuffer[dstOffset] = (byte)((255 - c) * (255 - k) / 255);
                            pixelBuffer[dstOffset + 1] = (byte)((255 - m) * (255 - k) / 255);
                            pixelBuffer[dstOffset + 2] = (byte)((255 - y) * (255 - k) / 255);
                            pixelBuffer[dstOffset + 3] = (smaskData is not null && i < smaskData.Length) ? smaskData[i] : (byte)255;
                        }
                    }
                    else if (imageData.Length >= expectedRgbSize)
                    {
                        // DCTDecode already converted CMYK to RGB
                        // ImageSharp handles Adobe CMYK convention internally
                        PdfLogger.Log(LogCategory.Images, $"DeviceCMYK->RGB first pixels: [{imageData[0]},{imageData[1]},{imageData[2]}] [{imageData[3]},{imageData[4]},{imageData[5]}] [{imageData[6]},{imageData[7]},{imageData[8]}]");

                        for (var i = 0; i < pixelCount; i++)
                        {
                            int srcOffset = i * 3;
                            int dstOffset = i * 4;
                            // Use RGB values directly - ImageSharp has already converted from CMYK
                            pixelBuffer[dstOffset] = imageData[srcOffset];
                            pixelBuffer[dstOffset + 1] = imageData[srcOffset + 1];
                            pixelBuffer[dstOffset + 2] = imageData[srcOffset + 2];
                            pixelBuffer[dstOffset + 3] = (smaskData is not null && i < smaskData.Length) ? smaskData[i] : (byte)255;
                        }
                    }
                    else
                    {
                        // Data too small for either format
                        return null;
                    }

                    var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, alphaType);
                    bitmap = new SKBitmap(imageInfo);
                    IntPtr bitmapPixels = bitmap.GetPixels();
                    if (bitmapPixels == IntPtr.Zero) return null;
                    Marshal.Copy(pixelBuffer, 0, bitmapPixels, pixelBuffer.Length);
                    bitmap.NotifyPixelsChanged();
                    break;
                }
                default:
                    // Unsupported color space/bits per component combination
                    return null;
            }

            return bitmap;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BITMAP ERROR] CreateBitmapFromPdfImage exception: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"[BITMAP ERROR] Stack trace: {ex.StackTrace}");
            PdfLogger.Log(LogCategory.Images, $"CreateBitmapFromPdfImage exception: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets the number of components for an ICCBased colorspace image
    /// </summary>
    private int GetIccBasedComponentCount(PdfImage image)
    {
        try
        {
            // Get the ColorSpace array from the image stream
            if (!image.Stream.Dictionary.TryGetValue(new PdfName("ColorSpace"), out PdfObject? csObj))
                return 3; // Default to RGB

            // Resolve indirect reference
            if (csObj is PdfIndirectReference reference && _document is not null)
                csObj = _document.ResolveReference(reference);

            // ICCBased colorspace is an array: [/ICCBased stream]
            if (csObj is not PdfArray { Count: >= 2 } csArray)
                return 3;

            // Get the ICC profile stream (index 1)
            PdfObject? streamObj = csArray[1];
            if (streamObj is PdfIndirectReference streamRef && _document is not null)
                streamObj = _document.ResolveReference(streamRef);

            if (streamObj is not PdfStream iccStream)
                return 3;

            // Get /N (number of components) from the ICC profile stream dictionary
            if (iccStream.Dictionary.TryGetValue(new PdfName("N"), out PdfObject? nObj) && nObj is PdfInteger nInt)
                return nInt.Value;

            return 3; // Default to RGB if /N not found
        }
        catch
        {
            return 3; // Default to RGB on error
        }
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
        using SKImage? image = _surface.Snapshot();
        using SKData? data = image.Encode(format, quality);
        using FileStream stream = File.OpenWrite(filePath);
        data.SaveTo(stream);
    }

    public void Dispose()
    {
        Clear(); // Clean up soft masks
        _surface.Dispose();
    }

    /// <summary>
    /// Clears the glyph path cache to free memory
    /// </summary>
    public static void ClearGlyphCache()
    {
        TextRenderer.ClearCache();
    }

    /// <summary>
    /// Gets the current number of cached glyph paths (approximate)
    /// </summary>
    public static int GetCacheCount()
    {
        return (int)TextRenderer.GetCachedGlyphCount();
    }

    /// <summary>
    /// Returns this instance since it is the underlying SkiaSharp render target.
    /// </summary>
    public SkiaSharpRenderTarget GetSkiaRenderTarget() => this;
}

/// <summary>
/// Lightweight IRenderTarget implementation for rendering pattern content to an existing surface.
/// Used internally by FillPathWithTilingPattern.
/// </summary>
internal class SkiaSharpRenderTargetForPattern : IRenderTarget, IDisposable
{
    private readonly SKSurface _surface;
    private readonly SKCanvas _canvas;
    private readonly double _patternWidth;
    private readonly double _patternHeight;
    private readonly Stack<SKMatrix> _stateStack = new();

    public int CurrentPageNumber => 1;

    public SkiaSharpRenderTargetForPattern(SKSurface surface, double patternWidth, double patternHeight)
    {
        _surface = surface;
        _canvas = surface.Canvas;
        _patternWidth = patternWidth;
        _patternHeight = patternHeight;
    }

    public void BeginPage(int pageNumber, double width, double height, double scale = 1.0, double cropOffsetX = 0, double cropOffsetY = 0, int rotation = 0)
    {
        // Pattern content uses pattern space coordinates, no additional transform needed
        // The canvas transformation is already set up by the caller
        // Rotation is not applicable to pattern space - patterns are defined in their own coordinate system
    }

    public void EndPage() { }

    public void Clear()
    {
        _canvas.Clear(SKColors.Transparent);
    }

    public void SaveState()
    {
        _stateStack.Push(_canvas.TotalMatrix);
        _canvas.Save();
    }

    public void RestoreState()
    {
        _canvas.Restore();
        if (_stateStack.Count > 0)
            _stateStack.Pop();
    }

    public void ApplyCtm(Matrix3x2 ctm)
    {
        var skMatrix = new SKMatrix(
            ctm.M11, ctm.M21, ctm.M31,
            ctm.M12, ctm.M22, ctm.M32,
            0, 0, 1);
        _canvas.Concat(ref skMatrix);
    }

    public void OnGraphicsStateChanged(PdfGraphicsState state) { }

    public void StrokePath(IPathBuilder path, PdfGraphicsState state)
    {
        if (path.IsEmpty) return;

        _canvas.Save();
        try
        {
            ApplyPathTransform(state);
            using SKPath skPath = ConvertToSkPath(path);
            using var paint = new SKPaint
            {
                Color = ConvertColor(state.ResolvedStrokeColor, state.ResolvedStrokeColorSpace),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(0.5f, (float)state.LineWidth)
            };
            _canvas.DrawPath(skPath, paint);
        }
        finally
        {
            _canvas.Restore();
        }
    }

    public void FillPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        if (path.IsEmpty) return;

        _canvas.Save();
        try
        {
            ApplyPathTransform(state);
            using SKPath skPath = ConvertToSkPath(path);
            // Set fill type based on PDF operator (evenOdd parameter)
            // Previously we forced EvenOdd when Y-flip was detected, but this causes overlapping
            // paths to create holes. The Y-flip is just a coordinate transform and doesn't
            // require changing the fill rule.
            skPath.FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
            using var paint = new SKPaint
            {
                Color = ConvertColor(state.ResolvedFillColor, state.ResolvedFillColorSpace),
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            _canvas.DrawPath(skPath, paint);
        }
        finally
        {
            _canvas.Restore();
        }
    }

    public void FillAndStrokePath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        FillPath(path, state, evenOdd);
        StrokePath(path, state);
    }

    public void FillPathWithTilingPattern(IPathBuilder path, PdfGraphicsState state, bool evenOdd,
        PdfTilingPattern pattern, Action<IRenderTarget> renderPatternContent)
    {
        // Nested patterns are rare - use solid fill as fallback
        FillPath(path, state, evenOdd);
    }

    public void SetClippingPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        if (path.IsEmpty) return;
        ApplyPathTransform(state);
        using SKPath skPath = ConvertToSkPath(path);
        skPath.FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
        _canvas.ClipPath(skPath);
    }

    public void DrawText(string text, List<double> glyphWidths, PdfGraphicsState state, PdfFont? font, List<int>? charCodes = null)
    {
        // For pattern content, use simple text rendering
        if (string.IsNullOrEmpty(text)) return;

        _canvas.Save();
        try
        {
            // Apply text transformation
            Matrix3x2 textMatrix = state.TextMatrix;
            Matrix3x2 ctm = state.Ctm;
            Matrix3x2 combined = textMatrix * ctm;

            var skMatrix = new SKMatrix(
                combined.M11, combined.M21, combined.M31,
                combined.M12, combined.M22, combined.M32,
                0, 0, 1);
            _canvas.Concat(ref skMatrix);

            // Calculate font size
            var effectiveSize = (float)(state.FontSize * Math.Sqrt(textMatrix.M21 * textMatrix.M21 + textMatrix.M22 * textMatrix.M22));
            if (effectiveSize < 0.1f) effectiveSize = 12f;

            using var paint = new SKPaint
            {
                Color = ConvertColor(state.ResolvedFillColor, state.ResolvedFillColorSpace),
                IsAntialias = true,
                TextSize = effectiveSize,
                Typeface = SKTypeface.Default
            };

            _canvas.DrawText(text, 0, 0, paint);
        }
        finally
        {
            _canvas.Restore();
        }
    }

    public void DrawImage(PdfImage image, PdfGraphicsState state)
    {
        // Pattern images - CTM has ALREADY been applied via ApplyCtm() when processing
        // the content stream's 'cm' operator. Don't apply it again here.
        // The canvas already has the correct transform to position and scale the image.
        _canvas.Save();
        try
        {
            // Log canvas state (CTM already applied)
            SKMatrix canvasMatrix = _canvas.TotalMatrix;
            PdfLogger.Log(LogCategory.Images, $"PATTERN DrawImage: Canvas matrix=[{canvasMatrix.ScaleX:F2},{canvasMatrix.SkewX:F2},{canvasMatrix.TransX:F2},{canvasMatrix.SkewY:F2},{canvasMatrix.ScaleY:F2},{canvasMatrix.TransY:F2}]");

            // Calculate where unit square corners will map to
            SKPoint p00 = canvasMatrix.MapPoint(new SKPoint(0, 0));
            SKPoint p11 = canvasMatrix.MapPoint(new SKPoint(1, 1));
            PdfLogger.Log(LogCategory.Images, $"PATTERN DrawImage: Unit (0,0) maps to ({p00.X:F2},{p00.Y:F2}), (1,1) maps to ({p11.X:F2},{p11.Y:F2})");

            // Try to decode and draw the image
            byte[] imageData = image.GetDecodedData();
            PdfLogger.Log(LogCategory.Images, $"PATTERN DrawImage: imageData.Length={imageData.Length}, ColorSpace={image.ColorSpace}, Size={image.Width}x{image.Height}");

            if (imageData.Length > 0)
            {
                using SKBitmap? bitmap = CreateBitmapFromImageData(image, imageData);
                PdfLogger.Log(LogCategory.Images, $"PATTERN DrawImage: bitmap is {(bitmap is null ? "NULL" : $"{bitmap.Width}x{bitmap.Height}")}");

                if (bitmap is not null)
                {
                    // Log some pixel values to verify bitmap content
                    SKColor pixel0 = bitmap.GetPixel(0, 0);
                    SKColor pixelMid = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
                    PdfLogger.Log(LogCategory.Images, $"PATTERN DrawImage: bitmap pixel(0,0)=({pixel0.Red},{pixel0.Green},{pixel0.Blue},{pixel0.Alpha}), pixel(mid)=({pixelMid.Red},{pixelMid.Green},{pixelMid.Blue},{pixelMid.Alpha})");

                    using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
                    _canvas.DrawBitmap(bitmap, new SKRect(0, 0, 1, 1), paint);
                    PdfLogger.Log(LogCategory.Images, "PATTERN DrawImage: Drew bitmap to (0,0,1,1)");
                }
            }
        }
        finally
        {
            _canvas.Restore();
        }
    }

    public void RenderSoftMask(string maskSubtype, Action<IRenderTarget> renderMaskContent)
    {
        // Soft masks in patterns - render content without mask
        renderMaskContent(this);
    }

    public void ClearSoftMask() { }

    public (int width, int height, double scale) GetPageDimensions()
    {
        return ((int)_patternWidth, (int)_patternHeight, 1.0);
    }

    public float MeasureTextWidth(string text, PdfGraphicsState state, PdfFont font)
    {
        // For pattern content, text measurement is rarely needed
        // Return a simple estimate based on character count
        if (string.IsNullOrEmpty(text))
            return 0f;

        // Calculate effective font size (same as DrawText)
        var textMatrixScaleY = (float)Math.Sqrt(state.TextMatrix.M21 * state.TextMatrix.M21 + state.TextMatrix.M22 * state.TextMatrix.M22);
        float effectiveFontSize = (float)state.FontSize * textMatrixScaleY;

        // Use rough character width estimate (average character is ~0.5em)
        float estimatedWidth = text.Length * effectiveFontSize * 0.5f;

        // Apply horizontal scaling
        float tHs = (float)state.HorizontalScaling / 100f;
        estimatedWidth *= tHs;

        return estimatedWidth;
    }

    public void Dispose() { }

    private void ApplyPathTransform(PdfGraphicsState state)
    {
        Matrix3x2 ctm = state.Ctm;
        var skMatrix = new SKMatrix(
            ctm.M11, ctm.M21, ctm.M31,
            ctm.M12, ctm.M22, ctm.M32,
            0, 0, 1);
        _canvas.Concat(ref skMatrix);
    }

    private static SKPath ConvertToSkPath(IPathBuilder pathBuilder)
    {
        var skPath = new SKPath();

        if (pathBuilder is not PathBuilder builder)
            return skPath;

        foreach (PathSegment segment in builder.Segments)
        {
            switch (segment)
            {
                case MoveToSegment moveTo:
                    skPath.MoveTo((float)moveTo.X, (float)moveTo.Y);
                    break;

                case LineToSegment lineTo:
                    skPath.LineTo((float)lineTo.X, (float)lineTo.Y);
                    break;

                case CurveToSegment curveTo:
                    skPath.CubicTo(
                        (float)curveTo.X1, (float)curveTo.Y1,
                        (float)curveTo.X2, (float)curveTo.Y2,
                        (float)curveTo.X3, (float)curveTo.Y3);
                    break;

                case ClosePathSegment:
                    skPath.Close();
                    break;
            }
        }
        return skPath;
    }

    private static SKColor ConvertColor(List<double> components, string colorSpace)
    {
        if (components.Count == 0)
            return SKColors.Black;

        switch (colorSpace)
        {
            case "DeviceGray":
            case "CalGray":
                var gray = (byte)(components[0] * 255);
                return new SKColor(gray, gray, gray);

            case "DeviceRGB":
            case "CalRGB":
                if (components.Count >= 3)
                {
                    return new SKColor(
                        (byte)(components[0] * 255),
                        (byte)(components[1] * 255),
                        (byte)(components[2] * 255));
                }
                break;

            case "DeviceCMYK":
                if (components.Count >= 4)
                {
                    double c = components[0], m = components[1], y = components[2], k = components[3];

                    // Improved CMYK to RGB conversion
                    var r = (byte)((1 - Math.Min(1.0, c * (1 - k) + k)) * 255);
                    var g = (byte)((1 - Math.Min(1.0, m * (1 - k) + k)) * 255);
                    var b = (byte)((1 - Math.Min(1.0, y * (1 - k) + k)) * 255);

                    // Debug logging for CMYK conversion (ConvertPatternColor)
                    PdfLogger.Log(LogCategory.Graphics,
                        $"CMYK→RGB (Pattern): CMYK=[{c:F2},{m:F2},{y:F2},{k:F2}] → RGB=({r},{g},{b})");

                    return new SKColor(r, g, b);
                }
                break;
        }

        return SKColors.Black;
    }

    private static SKBitmap? CreateBitmapFromImageData(PdfImage image, byte[] imageData)
    {
        int width = image.Width;
        int height = image.Height;
        int bitsPerComponent = image.BitsPerComponent;
        string colorSpace = image.ColorSpace;

        if (width <= 0 || height <= 0) return null;

        try
        {
            int pixelCount = width * height;
            var pixelBuffer = new byte[pixelCount * 4];

            switch (colorSpace)
            {
                case "DeviceGray":
                case "CalGray":
                    if (bitsPerComponent == 8 && imageData.Length >= pixelCount)
                    {
                        for (var i = 0; i < pixelCount; i++)
                        {
                            byte gray = imageData[i];
                            int offset = i * 4;
                            pixelBuffer[offset] = gray;
                            pixelBuffer[offset + 1] = gray;
                            pixelBuffer[offset + 2] = gray;
                            pixelBuffer[offset + 3] = 255;
                        }
                    }
                    else return null;
                    break;

                case "DeviceRGB":
                case "CalRGB":
                    int expectedRgb = pixelCount * 3;
                    if (imageData.Length >= expectedRgb)
                    {
                        for (var i = 0; i < pixelCount; i++)
                        {
                            int srcOffset = i * 3;
                            int dstOffset = i * 4;
                            pixelBuffer[dstOffset] = imageData[srcOffset];
                            pixelBuffer[dstOffset + 1] = imageData[srcOffset + 1];
                            pixelBuffer[dstOffset + 2] = imageData[srcOffset + 2];
                            pixelBuffer[dstOffset + 3] = 255;
                        }
                    }
                    else return null;
                    break;

                case "DeviceCMYK":
                    int expectedCmyk = pixelCount * 4;
                    if (imageData.Length >= expectedCmyk)
                    {
                        for (var i = 0; i < pixelCount; i++)
                        {
                            int srcOffset = i * 4;
                            int dstOffset = i * 4;

                            // Convert byte values (0-255) to 0-1 range
                            double c = imageData[srcOffset] / 255.0;
                            double m = imageData[srcOffset + 1] / 255.0;
                            double y = imageData[srcOffset + 2] / 255.0;
                            double k = imageData[srcOffset + 3] / 255.0;

                            // Improved CMYK to RGB conversion
                            pixelBuffer[dstOffset] = (byte)((1 - Math.Min(1.0, c * (1 - k) + k)) * 255);
                            pixelBuffer[dstOffset + 1] = (byte)((1 - Math.Min(1.0, m * (1 - k) + k)) * 255);
                            pixelBuffer[dstOffset + 2] = (byte)((1 - Math.Min(1.0, y * (1 - k) + k)) * 255);
                            pixelBuffer[dstOffset + 3] = 255;
                        }
                    }
                    else return null;
                    break;

                case "Indexed":
                {
                    // Handle indexed color images
                    byte[]? paletteData = image.GetIndexedPalette(out string? baseColorSpace, out int hival);
                    if (paletteData is null || baseColorSpace is null)
                        return null;

                    int componentsPerEntry = baseColorSpace switch
                    {
                        "DeviceRGB" => 3,
                        "DeviceGray" => 1,
                        _ => 3
                    };

                    // Convert indexed pixels to RGBA
                    for (var i = 0; i < pixelCount; i++)
                    {
                        if (i >= imageData.Length)
                            continue;

                        byte paletteIndex = imageData[i];
                        if (paletteIndex > hival)
                            paletteIndex = (byte)hival;

                        int paletteOffset = paletteIndex * componentsPerEntry;
                        int bufferOffset = i * 4;

                        byte r, g, b;
                        if (componentsPerEntry == 3 && paletteOffset + 2 < paletteData.Length)
                        {
                            r = paletteData[paletteOffset];
                            g = paletteData[paletteOffset + 1];
                            b = paletteData[paletteOffset + 2];
                        }
                        else if (componentsPerEntry == 1 && paletteOffset < paletteData.Length)
                        {
                            byte gray = paletteData[paletteOffset];
                            r = g = b = gray;
                        }
                        else
                        {
                            r = g = b = 0;
                        }

                        pixelBuffer[bufferOffset] = r;
                        pixelBuffer[bufferOffset + 1] = g;
                        pixelBuffer[bufferOffset + 2] = b;
                        pixelBuffer[bufferOffset + 3] = 255;
                    }
                    break;
                }

                default:
                    return null;
            }

            var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            IntPtr pixels = bitmap.GetPixels();
            if (pixels == IntPtr.Zero) return null;
            Marshal.Copy(pixelBuffer, 0, pixels, pixelBuffer.Length);
            bitmap.NotifyPixelsChanged();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
