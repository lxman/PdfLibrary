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
using SixLabors.ImageSharp.PixelFormats;
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
    private readonly GlyphToSKPathConverter _glyphConverter;
    private readonly Stack<SKMatrix> _stateStack;
    private readonly PdfDocument? _document;
    private double _pageWidth;
    private double _pageHeight;
    private double _scale = 1.0;
    private Matrix3x2 _initialTransform = Matrix3x2.Identity;

    // Soft mask support - track which state depth owns the current mask
    // When a graphics state sets an SMask, we start a layer and record the depth
    // The layer is composited with the mask ONLY when that specific state is restored
    private SKBitmap? _activeSoftMask;
    private int _softMaskOwnerDepth = -1;  // The state depth that owns the current soft mask (-1 = no mask)
    private int _currentStateDepth;        // Current graphics state nesting depth

    // Background color for the canvas (white for normal pages, transparent for mask rendering)
    private readonly SKColor _backgroundColor;

    // Static glyph path cache - shared across all render targets for efficiency
    private static readonly MemoryCache GlyphPathCache;
    private static readonly MemoryCacheEntryOptions CacheOptions;

    // Static font resolver - shared across all render targets
    private static readonly SystemFontResolver FontResolver;

    // Track the last document to detect when a new document is loaded
    // WeakReference prevents keeping old documents alive in memory
    private static WeakReference<PdfDocument>? _lastDocument;
    private static readonly object _documentLock = new();

    public int CurrentPageNumber { get; private set; }

    static SkiaSharpRenderTarget()
    {
        // Initialize cache with size limit (approx 10,000 glyphs)
        var cacheOptions = new MemoryCacheOptions
        {
            SizeLimit = 10000
        };
        GlyphPathCache = new MemoryCache(cacheOptions);

        // Cache entries expire after 10 minutes of non-use
        CacheOptions = new MemoryCacheEntryOptions()
            .SetSize(1)
            .SetSlidingExpiration(TimeSpan.FromMinutes(10))
            .RegisterPostEvictionCallback((key, value, reason, state) =>
            {
                // Dispose SKPath when evicted from cache
                if (value is SKPath path)
                    path.Dispose();
            });

        // Initialize the font resolver
        FontResolver = new SystemFontResolver();
    }

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
                    ClearGlyphCache();
                    _lastDocument = new WeakReference<PdfDocument>(document);
                }
            }
        }

        // Create SkiaSharp surface
        var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        _surface = SKSurface.Create(imageInfo);
        _canvas = _surface.Canvas;

        _glyphConverter = new GlyphToSKPathConverter();
        _stateStack = new Stack<SKMatrix>();

        // Clear to the background color
        _canvas.Clear(_backgroundColor);
    }

    // ==================== PAGE LIFECYCLE ====================

    public void BeginPage(int pageNumber, double width, double height, double scale = 1.0, double cropOffsetX = 0, double cropOffsetY = 0)
    {
        CurrentPageNumber = pageNumber;
        // Store the original PDF page dimensions (unscaled) for coordinate calculations
        // These are CropBox dimensions (the visible area)
        _pageWidth = width;
        _pageHeight = height;
        _scale = scale;

        PdfLogger.Log(LogCategory.Transforms, $"BeginPage: Page {pageNumber}, Size: {width}x{height}, Scale: {scale:F2}, CropOffset: ({cropOffsetX}, {cropOffsetY})");

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
        // Transform order (applied right to left):
        // 1. Translate by (-cropOffsetX, -cropOffsetY) - shift PDF coordinates so CropBox origin is at (0,0)
        // 2. Scale by (scale, -scale) - scales content and flips Y
        // 3. Translate by (0, height * scale) - moves origin to top-left of scaled output
        Matrix3x2 initialTransform = Matrix3x2.CreateTranslation((float)-cropOffsetX, (float)-cropOffsetY)
                                   * Matrix3x2.CreateScale((float)scale, (float)-scale)
                                   * Matrix3x2.CreateTranslation(0, (float)(height * scale));


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
        _stateStack.Clear();
        CurrentPageNumber = 0;
        ClearSoftMask();
        _currentStateDepth = 0;
    }

    // ==================== STATE MANAGEMENT ====================

    public void SaveState()
    {
        _canvas.Save();
        _stateStack.Push(_canvas.TotalMatrix);
        _currentStateDepth++;
    }

    public void RestoreState()
    {
        // Check if we're restoring the state that owns the soft mask
        // If so, we need to apply the mask to the layer and composite it
        if (_activeSoftMask is not null && _currentStateDepth == _softMaskOwnerDepth)
        {
            ApplySoftMaskToLayer();
            // Restore the layer that was started by SaveLayer in SetSoftMask
            _canvas.Restore();

            // Clear the mask ownership
            _activeSoftMask.Dispose();
            _activeSoftMask = null;
            _softMaskOwnerDepth = -1;
        }

        // Restore the regular canvas state (from SaveState's _canvas.Save())
        _canvas.Restore();
        if (_stateStack.Count > 0)
            _stateStack.Pop();

        _currentStateDepth--;
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
        // If there's already a mask, apply and clear it first
        if (_activeSoftMask is not null)
        {
            ClearSoftMask();
        }

        _activeSoftMask = maskBitmap;
        _softMaskOwnerDepth = _currentStateDepth;

        // Start a new layer for masked content
        // All subsequent drawing will go to this layer until the owning state is restored
        var layerBounds = new SKRect(0, 0, (float)(_pageWidth * _scale), (float)(_pageHeight * _scale));
        _canvas.SaveLayer(layerBounds, null);
    }

    /// <summary>
    /// Applies the soft mask to the current layer before restoring.
    /// Uses DstIn blend mode to mask the layer content with the soft mask's alpha channel.
    /// </summary>
    private void ApplySoftMaskToLayer()
    {
        if (_activeSoftMask is null)
            return;

        // Create a paint with DstIn blend mode
        // DstIn: Result = Dst × Src.Alpha - keeps destination color but multiplies by source alpha
        using var maskPaint = new SKPaint();
        maskPaint.BlendMode = SKBlendMode.DstIn;

        // Draw the mask bitmap - its alpha channel will be applied to the layer
        // The mask is in device coordinates, so reset the matrix temporarily
        SKMatrix currentMatrix = _canvas.TotalMatrix;
        _canvas.SetMatrix(SKMatrix.Identity);

        using SKImage? maskImage = SKImage.FromBitmap(_activeSoftMask);
        if (maskImage is not null)
        {
            _canvas.DrawImage(maskImage, 0, 0, maskPaint);
        }

        _canvas.SetMatrix(currentMatrix);
    }

    /// <summary>
    /// Clears the active soft mask.
    /// If a soft mask layer is active, composites it with the mask before clearing.
    /// </summary>
    public void ClearSoftMask()
    {
        if (_activeSoftMask is not null && _softMaskOwnerDepth >= 0)
        {
            // Apply the mask to whatever content has been drawn
            ApplySoftMaskToLayer();
            // Restore the layer that was started by SaveLayer in SetSoftMask
            _canvas.Restore();

            _activeSoftMask.Dispose();
            _activeSoftMask = null;
            _softMaskOwnerDepth = -1;
        }
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
        if (image is Image<Rgb24> rgb24)
        {
            var bitmap = new SKBitmap(rgb24.Width, rgb24.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            for (var y = 0; y < rgb24.Height; y++)
            {
                for (var x = 0; x < rgb24.Width; x++)
                {
                    Rgb24 pixel = rgb24[x, y];
                    bitmap.SetPixel(x, y, new SKColor(pixel.R, pixel.G, pixel.B, 255));
                }
            }
            return bitmap;
        }

        if (image is Image<Rgba32> rgba32)
        {
            var bitmap = new SKBitmap(rgba32.Width, rgba32.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            for (var y = 0; y < rgba32.Height; y++)
            {
                for (var x = 0; x < rgba32.Width; x++)
                {
                    Rgba32 pixel = rgba32[x, y];
                    bitmap.SetPixel(x, y, new SKColor(pixel.R, pixel.G, pixel.B, pixel.A));
                }
            }
            return bitmap;
        }

        if (image is Image<L8> l8)
        {
            var bitmap = new SKBitmap(l8.Width, l8.Height, SKColorType.Gray8, SKAlphaType.Opaque);
            for (var y = 0; y < l8.Height; y++)
            {
                for (var x = 0; x < l8.Width; x++)
                {
                    L8 pixel = l8[x, y];
                    bitmap.SetPixel(x, y, new SKColor(pixel.PackedValue, pixel.PackedValue, pixel.PackedValue));
                }
            }
            return bitmap;
        }

        throw new NotSupportedException($"ImageSharp pixel format {image.GetType().Name} not supported for conversion to SKBitmap");
    }

    // ==================== TEXT RENDERING ====================

    public void DrawText(string text, List<double> glyphWidths, PdfGraphicsState state, PdfFont? font, List<int>? charCodes = null)
    {
        if (string.IsNullOrEmpty(text) || state.FontName is null)
        {
            PdfLogger.Log(LogCategory.Text, $"DRAWTEXT-SKIPPED: text.IsNullOrEmpty={string.IsNullOrEmpty(text)}, FontName={state.FontName}");
            return;
        }

        PdfLogger.Log(LogCategory.Text, $"DRAWTEXT: text='{text}', FontName={state.FontName}, FontSize={state.FontSize}, TextMatrix=[{state.TextMatrix.M11},{state.TextMatrix.M12},{state.TextMatrix.M21},{state.TextMatrix.M22},{state.TextMatrix.M31},{state.TextMatrix.M32}], font={font?.GetType().Name ?? "null"}");

        _canvas.Save();

        try
        {
            // Note: We no longer apply glyph transformation to the canvas here.
            // Instead, each glyph path is transformed individually with the full glyph matrix.
            // Canvas already has (displayMatrix × CTM) from ApplyCtm().

            // Convert fill color with alpha from graphics state
            SKColor fillColor = ConvertColor(state.ResolvedFillColor, state.ResolvedFillColorSpace);
            fillColor = ApplyAlpha(fillColor, state.FillAlpha);

            PdfLogger.Log(LogCategory.Text, $"DRAWTEXT-COLOR: ResolvedColorSpace={state.ResolvedFillColorSpace}, ResolvedColor=[{string.Join(",", state.ResolvedFillColor.Select(c => c.ToString("F2")))}], SKColor=({fillColor.Red},{fillColor.Green},{fillColor.Blue},{fillColor.Alpha})");

            using var paint = new SKPaint();
            paint.Color = fillColor;
            paint.IsAntialias = true;
            paint.Style = SKPaintStyle.Fill;

            // Try to render using embedded font glyph outlines
            if (font is not null && TryRenderWithGlyphOutlines(text, glyphWidths, state, font, paint, charCodes))
            {
                double rotationRad = Math.Atan2(state.TextMatrix.M12, state.TextMatrix.M11);
                double rotationDeg = rotationRad * (180.0 / Math.PI);
                PdfLogger.Log(LogCategory.Text, $"DRAWTEXT-PATH: Using embedded glyph outlines for '{text}' (rotation={rotationDeg:F1}°)");
                return;
            }

            double fallbackRotationRad = Math.Atan2(state.TextMatrix.M12, state.TextMatrix.M11);
            double fallbackRotationDeg = fallbackRotationRad * (180.0 / Math.PI);
            PdfLogger.Log(LogCategory.Text, $"DRAWTEXT-PATH: Using fallback rendering for '{text}' (rotation={fallbackRotationDeg:F1}°)");

            // Fallback: render each character individually using PDF glyph widths
            // This preserves correct spacing even when using a substitute font

            // In PDF, the actual visual font size is FontSize * TextMatrix scaling.
            // Common pattern: "1 Tf" (FontSize=1) with "60 0 0 60 x y Tm" (TextMatrix scales by 60)
            // The effective font size = FontSize * sqrt(M11^2 + M12^2) for the scaling factor
            // For simple scaling (no rotation), this is just FontSize * M11
            var textMatrixScaleX = (float)Math.Sqrt(state.TextMatrix.M11 * state.TextMatrix.M11 + state.TextMatrix.M12 * state.TextMatrix.M12);
            var textMatrixScaleY = (float)Math.Sqrt(state.TextMatrix.M21 * state.TextMatrix.M21 + state.TextMatrix.M22 * state.TextMatrix.M22);
            float effectiveFontSize = (float)state.FontSize * textMatrixScaleY; // Use Y scale for font size

            // HorizontalScaling is stored as a percentage (100 = 100% = 1.0 scale)
            float tHs = (float)state.HorizontalScaling / 100f;
            var tRise = (float)state.TextRise;

            // The fallback matrix only handles position transforms now
            // Font size is handled by the SKFont, TextMatrix scaling is extracted separately
            // We need a matrix that positions characters without the font size scaling
            // Since we extracted the scale into effectiveFontSize, we use normalized TextMatrix for positioning
            float posScaleX = textMatrixScaleX > 0 ? state.TextMatrix.M11 / textMatrixScaleX : 1;
            float posScaleY = textMatrixScaleY > 0 ? state.TextMatrix.M22 / textMatrixScaleY : 1;

            // Position matrix: applies text matrix for position, preserving rotation
            // Note: glyphWidths from PdfRenderer already include:
            //   - FontSize/1000 scaling (to convert from glyph units to user space)
            //   - HorizontalScaling/100 (tHs)
            // We use the actual text matrix components (not just magnitudes) to preserve rotation direction
            // This ensures character advances follow the rotated baseline correctly
            var textPositionMatrix = new Matrix3x2(
                state.TextMatrix.M11, state.TextMatrix.M12,
                state.TextMatrix.M21, state.TextMatrix.M22,
                0, tRise * textMatrixScaleY
            );

            Matrix3x2 fallbackMatrix = textPositionMatrix;
            // Apply translation from TextMatrix, adding to the rise translation
            fallbackMatrix.M31 = state.TextMatrix.M31;
            fallbackMatrix.M32 = state.TextMatrix.M32 + tRise * textMatrixScaleY;

            // Extract rotation angle from text matrix (in radians)
            // For a rotation matrix: M11=cos(θ), M12=sin(θ), M21=-sin(θ), M22=cos(θ)
            var rotationAngleRadians = (float)Math.Atan2(state.TextMatrix.M12, state.TextMatrix.M11);
            float rotationAngleDegrees = rotationAngleRadians * (180f / (float)Math.PI);

            PdfLogger.Log(LogCategory.Text,
                $"[ROTATION-DEBUG] Text='{text}' TextMatrix=[{state.TextMatrix.M11:F4},{state.TextMatrix.M12:F4},{state.TextMatrix.M21:F4},{state.TextMatrix.M22:F4},{state.TextMatrix.M31:F2},{state.TextMatrix.M32:F2}] Rotation={rotationAngleDegrees:F2}°");

            // Determine font style and family from font descriptor and name
            SKFontStyle? fontStyle = SKFontStyle.Normal;
            var fallbackFontFamily = "Arial"; // Default to sans-serif

            if (font is not null)
            {
                PdfFontDescriptor? descriptor = font.GetDescriptor();
                var isBold = false;
                var isItalic = false;
                var isSerif = false;
                var isMonospace = false;

                if (descriptor is not null)
                {
                    isBold = descriptor.IsBold;
                    isItalic = descriptor.IsItalic;
                    isSerif = descriptor.IsSerif;
                    isMonospace = descriptor.IsFixedPitch;

                    // Also use StemV as a heuristic for bold detection
                    if (descriptor.StemV >= 120)
                        isBold = true;
                }

                // Also check the font name for style and family hints
                string baseName = font.BaseFont;
                if (baseName.Contains("Bold", StringComparison.OrdinalIgnoreCase))
                    isBold = true;
                if (baseName.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
                    baseName.Contains("Oblique", StringComparison.OrdinalIgnoreCase))
                    isItalic = true;

                // Detect monospace fonts by name
                if (baseName.Contains("Courier", StringComparison.OrdinalIgnoreCase) ||
                    baseName.Contains("Consolas", StringComparison.OrdinalIgnoreCase) ||
                    baseName.Contains("Monaco", StringComparison.OrdinalIgnoreCase) ||
                    baseName.Contains("Mono", StringComparison.OrdinalIgnoreCase))
                {
                    isMonospace = true;
                }

                // Detect serif fonts by name
                if (baseName.Contains("Times", StringComparison.OrdinalIgnoreCase) ||
                    baseName.Contains("Serif", StringComparison.OrdinalIgnoreCase) ||
                    baseName.Contains("Georgia", StringComparison.OrdinalIgnoreCase) ||
                    baseName.Contains("Palatino", StringComparison.OrdinalIgnoreCase) ||
                    baseName.Contains("Garamond", StringComparison.OrdinalIgnoreCase) ||
                    baseName.Contains("Cambria", StringComparison.OrdinalIgnoreCase) ||
                    baseName.Contains("Bodoni", StringComparison.OrdinalIgnoreCase) ||
                    baseName.Contains("Century", StringComparison.OrdinalIgnoreCase) ||
                    baseName.Contains("Bookman", StringComparison.OrdinalIgnoreCase))
                {
                    isSerif = true;
                }

                // Choose fallback font family based on font classification
                // Monospace takes priority, then serif vs sans-serif
                // Use FontResolver to get metric-compatible fonts (Nimbus, Liberation, TeX Gyre)
                if (isMonospace)
                    fallbackFontFamily = FontResolver.GetResolvedFontName(FontCategory.Monospace);
                else if (isSerif)
                    fallbackFontFamily = FontResolver.GetResolvedFontName(FontCategory.Serif);
                else
                    fallbackFontFamily = FontResolver.GetResolvedFontName(FontCategory.SansSerif);

                switch (isBold)
                {
                    case true when isItalic:
                        fontStyle = SKFontStyle.BoldItalic;
                        break;
                    case true:
                        fontStyle = SKFontStyle.Bold;
                        break;
                    default:
                    {
                        if (isItalic)
                            fontStyle = SKFontStyle.Italic;
                        break;
                    }
                }
            }

            // Use effective font size (FontSize * TextMatrix scaling) for visible text
            using var fallbackFont = new SKFont(SKTypeface.FromFamilyName(fallbackFontFamily, fontStyle), effectiveFontSize);

            // Determine text rendering mode
            // PDF Text Rendering Modes:
            // 0 = Fill, 1 = Stroke, 2 = Fill then Stroke, 3 = Invisible
            // 4-7 = Same as 0-3 but also add to clipping path
            int renderMode = state.RenderingMode;
            bool shouldFill = renderMode == 0 || renderMode == 2 || renderMode == 4 || renderMode == 6;
            bool shouldStroke = renderMode == 1 || renderMode == 2 || renderMode == 5 || renderMode == 6;
            bool isInvisible = renderMode == 3 || renderMode == 7;

            if (isInvisible)
            {
                // Don't render invisible text (used for searchable OCR layers)
                return;
            }

            // Calculate CTM scaling factor for stroke width
            Matrix3x2 ctm = state.Ctm;
            double ctmScale = Math.Sqrt(Math.Abs(ctm.M11 * ctm.M22 - ctm.M12 * ctm.M21));
            // Note: Small scale factors are valid - they correctly scale large PDF values to device pixels

            // Prepare stroke paint if needed
            SKPaint? strokePaint = null;
            if (shouldStroke)
            {
                SKColor strokeColor = ConvertColor(state.ResolvedStrokeColor, state.ResolvedStrokeColorSpace);
                strokeColor = ApplyAlpha(strokeColor, state.StrokeAlpha);

                double scaledStrokeWidth = state.LineWidth * ctmScale;
                if (scaledStrokeWidth < 0.5) scaledStrokeWidth = 0.5; // Minimum 0.5 device pixel

                strokePaint = new SKPaint();
                strokePaint.Color = strokeColor;
                strokePaint.IsAntialias = true;
                strokePaint.Style = SKPaintStyle.Stroke;
                strokePaint.StrokeWidth = (float)scaledStrokeWidth;
            }

            // Update fill paint style based on rendering mode
            if (!shouldFill)
            {
                // If we're not filling, don't use the fill paint
                paint.Style = SKPaintStyle.Stroke; // Just to prevent accidental fill
            }

            try
            {
                float currentX = 0;
                for (var i = 0; i < text.Length; i++)
                {
                    // Transform character position through fallback matrix
                    // Position is in PDF coordinates (includes TextMatrix translation)
                    Vector2 position = Vector2.Transform(new Vector2(currentX, 0), fallbackMatrix);

                    // Use PDF-specified width for positioning (in glyph space units, typically 1/1000 em)
                    // This maintains correct spacing as specified by the PDF regardless of fallback font metrics
                    float pdfWidth = i < glyphWidths.Count ? (float)glyphWidths[i] : 0;
                    var ch = text[i].ToString();

                    // DIAGNOSTIC: Measure actual SkiaSharp glyph width vs PDF width
                    float actualWidth = fallbackFont.MeasureText(ch);
                    if (i < 3 && Math.Abs(actualWidth - pdfWidth) > 0.1f)
                    {
                        PdfLogger.Log(LogCategory.Text,
                            $"  [WIDTH-MISMATCH] '{text}' Char[{i}]='{ch}': PDF={pdfWidth:F3}, Actual={actualWidth:F3}, Diff={actualWidth - pdfWidth:F3}");
                    }

                    // The canvas has a Y-flip applied, which makes text render upside down
                    // We need to apply a local transformation at the text position
                    // The position is already transformed by fallbackMatrix (which includes text matrix rotation)
                    // So we need to "undo" the rotation, apply Y-flip, then reapply rotation
                    _canvas.Save();
                    _canvas.Translate(position.X, position.Y);

                    // Extract rotation angle from fallbackMatrix to undo it
                    // For vertical text, fallbackMatrix has the rotation baked in
                    // We need to rotate back to upright, flip Y, then rotate forward again
                    var localRotationRad = (float)Math.Atan2(fallbackMatrix.M12, fallbackMatrix.M11);
                    float localRotationDeg = localRotationRad * (180f / (float)Math.PI);

                    if (i == 0) // Log for first character only to avoid spam
                    {
                        PdfLogger.Log(LogCategory.Text,
                            $"[ROTATION-DEBUG] Char '{ch}' pos=({position.X:F2},{position.Y:F2}) localRot={localRotationDeg:F2}° fallbackMatrix=[{fallbackMatrix.M11:F4},{fallbackMatrix.M12:F4},{fallbackMatrix.M21:F4},{fallbackMatrix.M22:F4}]");
                    }

                    // Apply transformations in order:
                    // 1. Rotate to match text orientation
                    // 2. Apply Y-flip (in rotated space) and horizontal scaling
                    // NOTE: We do NOT scale glyphs to match PDF widths - this would distort them visually.
                    // Instead, we render glyphs at their natural width and use PDF widths only for spacing.
                    _canvas.RotateDegrees(localRotationDeg);  // Apply rotation
                    _canvas.Scale(tHs, -1);  // Horizontal scaling and Y-flip (in rotated space)

                    // Draw based on rendering mode
                    if (shouldFill)
                    {
                        _canvas.DrawText(ch, 0, 0, fallbackFont, paint);
                    }

                    if (shouldStroke && strokePaint != null)
                    {
                        _canvas.DrawText(ch, 0, 0, fallbackFont, strokePaint);
                    }

                    _canvas.Restore();

                    // Advance by the PDF-specified width
                    // PDF widths are in glyph space (1/1000 em units), already scaled by effectiveFontSize/1000
                    // through the fallbackMatrix transformation
                    currentX += pdfWidth;
                }
            }
            finally
            {
                strokePaint?.Dispose();
            }
        }
        finally
        {
            _canvas.Restore();
        }
    }

    private bool TryRenderWithGlyphOutlines(string text, List<double> glyphWidths, PdfGraphicsState state, PdfFont font, SKPaint paint, List<int>? charCodes)
    {
        PdfLogger.Log(LogCategory.Text, $"######## GLYPH-ENTRY: TryRenderWithGlyphOutlines called for text='{text}' ########");
        try
        {
            // Check if the font should be rendered as bold (for synthetic bold)
            var applyBold = false;
            PdfFontDescriptor? descriptor = font.GetDescriptor();
            if (descriptor is not null)
            {
                // Check font flags for bold, or check the font name for "Bold"
                bool isBoldFlag = descriptor.IsBold;
                bool isBoldName = font.BaseFont?.Contains("Bold", StringComparison.OrdinalIgnoreCase) == true;

                // Also use StemV as heuristic - higher values indicate bolder fonts
                // Regular fonts typically have StemV < 100, bold fonts > 150
                // Values 120-150 suggest semi-bold or bold variants without "Bold" in the name
                bool isBoldStemV = descriptor.StemV >= 120;

                applyBold = isBoldFlag || isBoldName || isBoldStemV;
            }

            // Get embedded font metrics
            EmbeddedFontMetrics? embeddedMetrics = font.GetEmbeddedMetrics();
            if (embeddedMetrics is not { IsValid: true })
                return false;

            PdfLogger.Log(LogCategory.Text, $"GLYPH-METRICS: Got valid embedded metrics, IsCffFont={embeddedMetrics.IsCffFont}, IsType1Font={embeddedMetrics.IsType1Font}");

            // Calculate the horizontal scaling factor early so we can use it for character advances
            // HorizontalScaling is stored as a percentage (100 = 100% = 1.0 scale)
            float tHs = (float)state.HorizontalScaling / 100f;

            // Position for rendering glyphs
            double currentX = 0;

            // Iterate over character codes, not Unicode characters
            // One character code can decode to multiple Unicode chars (e.g., ligatures)
            int loopCount = charCodes?.Count ?? text.Length;

            PdfLogger.Log(LogCategory.Text, $"GLYPH-LOOP: About to render {loopCount} characters, text.Length={text.Length}, charCodes={(charCodes == null ? "null" : charCodes.Count.ToString())}");

            for (var i = 0; i < loopCount; i++)
            {
                PdfLogger.Log(LogCategory.Text, $"GLYPH-LOOP-ITER: Starting iteration {i} of {loopCount}");

                // Get character code - either from original PDF codes or fall back to Unicode
                ushort charCode = charCodes is not null && i < charCodes.Count
                    ? (ushort)charCodes[i]
                    : text[i];

                // Get corresponding character for logging (may not match 1:1 due to ligatures)
                char displayChar = i < text.Length ? text[i] : '?';

                ushort glyphId;
                string? resolvedGlyphName = null; // Track glyph name for Type1 fonts

                // For CFF and Type1 fonts without cmap, use glyph name mapping via the PDF encoding
                if ((embeddedMetrics.IsCffFont || embeddedMetrics.IsType1Font) && font.Encoding is not null)
                {
                    resolvedGlyphName = font.Encoding.GetGlyphName(charCode);
                    glyphId = resolvedGlyphName is not null
                        ? embeddedMetrics.GetGlyphIdByName(resolvedGlyphName)
                        // Fall back to direct lookup
                        : embeddedMetrics.GetGlyphId(charCode);
                }
                // For Type0 fonts with embedded Type1 data, use ToUnicode → glyph name mapping
                else if (font is Type0Font type0Font && embeddedMetrics.IsType1Font && type0Font.ToUnicode is not null)
                {
                    // Use ToUnicode CMap to get the Unicode character
                    string? unicode = type0Font.ToUnicode.Lookup(charCode);
                    if (unicode is not null)
                    {
                        // Map Unicode to PostScript glyph name via Adobe Glyph List
                        resolvedGlyphName = AdobeGlyphList.GetGlyphName(unicode);
                        if (resolvedGlyphName is not null)
                        {
                            glyphId = embeddedMetrics.GetGlyphIdByName(resolvedGlyphName);
                        }
                        else
                        {
                            // No AGL mapping, try using the character itself as the glyph name (works for basic ASCII)
                            if (unicode.Length == 1 && char.IsAscii(unicode[0]))
                            {
                                resolvedGlyphName = unicode;
                                glyphId = embeddedMetrics.GetGlyphIdByName(resolvedGlyphName);
                            }
                            else
                            {
                                glyphId = 0;
                            }
                        }
                    }
                    else
                    {
                        // No ToUnicode mapping, fall back to CIDToGIDMap
                        if (type0Font.DescendantFont is CidFont cidFont)
                        {
                            glyphId = (ushort)cidFont.MapCidToGid(charCode);
                        }
                        else
                        {
                            glyphId = embeddedMetrics.GetGlyphId(charCode);
                        }
                    }
                }
                else
                {
                    // For Type0/CID fonts, map CID to GID using CIDToGIDMap
                    if (font is Type0Font { DescendantFont: CidFont cidFont })
                    {
                        PdfLogger.Log(LogCategory.Text, $"GLYPH-PATH: Type0/CID font path, charCode={charCode}");
                        // For Type0 fonts, use CIDToGIDMap directly - the mapped value IS the glyph ID
                        int mappedGid = cidFont.MapCidToGid(charCode);
                        glyphId = (ushort)mappedGid;
                        PdfLogger.Log(LogCategory.Text, $"GLYPH-PATH: Type0 MapCidToGid returned glyphId={glyphId}");
                    }
                    else
                    {
                        PdfLogger.Log(LogCategory.Text, $"GLYPH-PATH: TrueType cmap path, charCode={charCode}, font.GetType()={font.GetType().Name}");
                        // For other fonts, use cmap lookup
                        glyphId = embeddedMetrics.GetGlyphId(charCode);
                        PdfLogger.Log(LogCategory.Text, $"GLYPH-PATH: GetGlyphId returned glyphId={glyphId}");
                    }
                }

                if (glyphId == 0)
                {
                    PdfLogger.Log(LogCategory.Text, $"GLYPH-SKIP-ZERO: char='{displayChar}' (charCode={charCode}), glyphId=0, skipping");
                    // Glyph not found, skip this character
                    if (i < glyphWidths.Count)
                        currentX += glyphWidths[i];
                    continue;
                }

                // Extract glyph outline
                // For Type1 fonts, use name-based lookup for better accuracy
                GlyphOutline? glyphOutline = embeddedMetrics.IsType1Font && resolvedGlyphName is not null
                    ? embeddedMetrics.GetGlyphOutlineByName(resolvedGlyphName)
                    : embeddedMetrics.GetGlyphOutline(glyphId);

                if (glyphOutline is null)
                {
                    PdfLogger.Log(LogCategory.Text, $"GLYPH-SKIP-NULL: char='{displayChar}' (charCode={charCode}), glyphId={glyphId}, glyphOutline is null");
                    // Check if this glyph has metrics (advance width > 0) but no outline
                    // This happens in subset fonts where the glyph outline was stripped
                    float glyphWidth = i < glyphWidths.Count ? (float)glyphWidths[i] : 0;
                    ushort advanceWidth = embeddedMetrics.GetAdvanceWidth(glyphId);

                    // If this looks like an em dash (charCode 151) or similar punctuation with width,
                    // draw a fallback rectangle
                    if (charCode == 151 && glyphWidth > 0.1f) // Em dash
                    {
                        // Draw em dash as a horizontal line/rectangle
                        // Em dash is typically at about 40% of the em height, with height about 5-8% of em
                        float emDashY = (float)state.FontSize * 0.35f;  // Position at ~35% up from baseline
                        float emDashHeight = (float)state.FontSize * 0.06f;  // ~6% of font size
                        float emDashWidth = glyphWidth * (float)state.FontSize;  // Full width scaled by font size

                        // Create rectangle path for em dash
                        using var emDashPath = new SKPath();
                        // Note: Y is negative because glyph coordinates are Y-up but we flip
                        emDashPath.AddRect(new SKRect(0, -emDashY - emDashHeight, emDashWidth, -emDashY));

                        // Apply glyph transformation
                        var emDashTRise = (float)state.TextRise;
                        var emDashTextStateMatrix = new Matrix3x2(
                            tHs, 0,
                            0, 1,
                            0, emDashTRise
                        );
                        Matrix3x2 emDashGlyphMatrix = emDashTextStateMatrix * state.TextMatrix;
                        var emDashTranslationMatrix = Matrix3x2.CreateTranslation((float)currentX, 0);
                        Matrix3x2 emDashFullMatrix = emDashTranslationMatrix * emDashGlyphMatrix;

                        var emDashMatrix = new SKMatrix
                        {
                            ScaleX = emDashFullMatrix.M11,
                            SkewX = emDashFullMatrix.M21,
                            TransX = emDashFullMatrix.M31,
                            SkewY = emDashFullMatrix.M12,
                            ScaleY = -emDashFullMatrix.M22,  // Flip Y
                            TransY = emDashFullMatrix.M32,
                            Persp0 = 0,
                            Persp1 = 0,
                            Persp2 = 1
                        };

                        emDashPath.Transform(emDashMatrix);
                        _canvas.DrawPath(emDashPath, paint);
                    }

                    if (i < glyphWidths.Count)
                        currentX += glyphWidths[i];
                    continue;
                }

                if (glyphOutline.IsEmpty)
                {
                    PdfLogger.Log(LogCategory.Text, $"GLYPH-SKIP-EMPTY: char='{displayChar}' (charCode={charCode}), glyphId={glyphId}, glyphOutline.IsEmpty=true");
                    // Empty glyph (e.g., space), just advance
                    if (i < glyphWidths.Count)
                        currentX += glyphWidths[i];
                    continue;
                }

                // Create a cache key: font name + glyph ID + font size (rounded for precision)
                string fontKey = font.BaseFont ?? "unknown";
                var roundedSize = (int)(state.FontSize * 10); // 0.1pt precision
                var cacheKey = $"{fontKey}_{glyphId}_{roundedSize}";

                // Try to get from the cache first
                SKPath glyphPath;
                if (GlyphPathCache.TryGetValue(cacheKey, out SKPath? cachedPath) && cachedPath is not null)
                {
                    glyphPath = new SKPath(cachedPath);
                }
                else
                {
                    // Convert glyph outline to SKPath
                    // Check if this is a CFF or Type1 font for proper cubic Bezier rendering
                    if (embeddedMetrics.IsCffFont)
                    {
                        FontParser.Tables.Cff.GlyphOutline? cffOutline = embeddedMetrics.GetCffGlyphOutlineDirect(glyphId);
                        if (cffOutline is not null)
                        {
                            glyphPath = _glyphConverter.ConvertCffToPath(
                                cffOutline,
                                (float)state.FontSize,
                                embeddedMetrics.UnitsPerEm
                            );
                        }
                        else
                        {
                            // Fall back to contour-based conversion
                            glyphPath = _glyphConverter.ConvertToPath(
                                glyphOutline,
                                (float)state.FontSize,
                                embeddedMetrics.UnitsPerEm
                            );
                        }
                    }
                    else if (embeddedMetrics.IsType1Font && resolvedGlyphName is not null)
                    {
                        // Type1 font - use cubic Bezier conversion (same as CFF)
                        FontParser.Tables.Cff.GlyphOutline? type1Outline = embeddedMetrics.GetType1GlyphOutlineDirect(resolvedGlyphName);
                        if (type1Outline is not null)
                        {
                            glyphPath = _glyphConverter.ConvertCffToPath(
                                type1Outline,
                                (float)state.FontSize,
                                embeddedMetrics.UnitsPerEm
                            );
                        }
                        else
                        {
                            // Fall back to contour-based conversion
                            glyphPath = _glyphConverter.ConvertToPath(
                                glyphOutline,
                                (float)state.FontSize,
                                embeddedMetrics.UnitsPerEm
                            );
                        }
                    }
                    else
                    {
                        // TrueType font - use quadratic Bezier conversion
                        glyphPath = _glyphConverter.ConvertToPath(
                            glyphOutline,
                            (float)state.FontSize,
                            embeddedMetrics.UnitsPerEm
                        );
                    }

                    // Cache the path (clone it since we'll transform the original)
                    var pathToCache = new SKPath(glyphPath);
                    GlyphPathCache.Set(cacheKey, pathToCache, CacheOptions);
                }

                // IMPORTANT: The glyph converter (ConvertToPath/ConvertCffToPath) already scales
                // the glyph by FontSize when converting from font units to device units.
                // Therefore, we should NOT include FontSize in the transformation matrix here,
                // or we'll get double font size scaling!
                //
                // This matches the fallback rendering approach (see line 223) which also
                // excludes FontSize because SKFont already has it applied.
                //
                // Glyph transformation matrix should be: [[Th, 0, 0], [0, 1, 0], [0, Trise, 1]] × Tm
                // (Note: NO FontSize scaling - that's already in the glyph path!)

                // tHs is already calculated earlier for character advances
                var tRise = (float)state.TextRise;

                var textStateMatrix = new Matrix3x2(
                    tHs, 0,      // Scale X by HorizontalScale only (NO FontSize - already in path!)
                    0, 1,        // No Y scaling (FontSize already in path!)
                    0, tRise     // Translate Y by TextRise
                );

                // Multiply by text matrix to get the complete glyph transformation
                Matrix3x2 glyphMatrix = textStateMatrix * state.TextMatrix;

                // Add translation for the current glyph position (in text space)
                // The translation needs to be applied BEFORE the glyph transformation
                // so it gets scaled by the TextMatrix
                var translationMatrix = Matrix3x2.CreateTranslation((float)currentX, 0);
                Matrix3x2 fullGlyphMatrix = translationMatrix * glyphMatrix;

                // Convert to SKMatrix and apply to path
                // Apply rotation-aware Y-flip compensation
                // The canvas has a global Y-flip (y → -y) applied in BeginPage
                //
                // Global Y-flip transforms matrix [M11, M12, M21, M22] to effectively [M11, -M12, -M21, M22]
                // because it negates all y-coordinates in the transformation
                //
                // To compensate, we pre-negate the components that will be flipped

                // Detect rotation angle from the matrix
                double rotationRad = Math.Atan2(fullGlyphMatrix.M12, fullGlyphMatrix.M11);
                double rotationDeg = rotationRad * (180.0 / Math.PI);

                // Normalize rotation to [-180, 180]
                while (rotationDeg > 180) rotationDeg -= 360;
                while (rotationDeg < -180) rotationDeg += 360;

                // Determine if text is primarily vertical (rotation near ±90°)
                bool isVertical = Math.Abs(Math.Abs(rotationDeg) - 90) < 45;

                float skewY, skewX, scaleY;
                if (isVertical)
                {
                    // For vertical text, the Y-flip affects M21 (SkewX in SKMatrix)
                    // The character's right direction maps to (M11, M21), and the Y-component M21 gets flipped
                    // Pre-negate M21 only to compensate
                    skewY = fullGlyphMatrix.M12;
                    skewX = -fullGlyphMatrix.M21;
                    scaleY = fullGlyphMatrix.M22;
                    PdfLogger.Log(LogCategory.Text, $"GLYPH-YFLIP: char='{displayChar}', rotation={rotationDeg:F1}°, VERTICAL, negating M21 only");
                }
                else
                {
                    // For horizontal text, only M22 needs compensation
                    skewY = fullGlyphMatrix.M12;
                    skewX = fullGlyphMatrix.M21;
                    scaleY = -fullGlyphMatrix.M22;
                    PdfLogger.Log(LogCategory.Text, $"GLYPH-YFLIP: char='{displayChar}', rotation={rotationDeg:F1}°, HORIZONTAL, negating M22");
                }

                var skGlyphMatrix = new SKMatrix
                {
                    ScaleX = fullGlyphMatrix.M11,
                    SkewY = skewY,
                    SkewX = skewX,
                    ScaleY = scaleY,
                    TransX = fullGlyphMatrix.M31,
                    TransY = fullGlyphMatrix.M32,
                    Persp0 = 0,
                    Persp1 = 0,
                    Persp2 = 1
                };
                glyphPath.Transform(skGlyphMatrix);

                // Render the glyph based on text rendering mode
                // PDF Text Rendering Modes:
                // 0 = Fill, 1 = Stroke, 2 = Fill then Stroke, 3 = Invisible
                // 4-7 = Same as 0-3 but also add to clipping path
                int renderMode = state.RenderingMode;
                bool shouldFill = renderMode == 0 || renderMode == 2 || renderMode == 4 || renderMode == 6;
                bool shouldStroke = renderMode == 1 || renderMode == 2 || renderMode == 5 || renderMode == 6;
                bool isInvisible = renderMode == 3 || renderMode == 7;

                PdfLogger.Log(LogCategory.Text, $"GLYPH-MODE: char='{displayChar}', renderMode={renderMode}, shouldFill={shouldFill}, shouldStroke={shouldStroke}, isInvisible={isInvisible}");

                if (!isInvisible)
                {
                    // Calculate CTM scaling factor for stroke width
                    Matrix3x2 ctm = state.Ctm;
                    double ctmScale = Math.Sqrt(Math.Abs(ctm.M11 * ctm.M22 - ctm.M12 * ctm.M21));

                    if (shouldFill)
                    {
                        // Transform path to device coordinates and draw with identity matrix
                        SKMatrix canvasMatrix = _canvas.TotalMatrix;
                        PdfLogger.Log(LogCategory.Text, $"GLYPH-RENDER: char='{displayChar}', canvasMatrix=[{canvasMatrix.ScaleX:F2},{canvasMatrix.SkewX:F2},{canvasMatrix.TransX:F2};{canvasMatrix.SkewY:F2},{canvasMatrix.ScaleY:F2},{canvasMatrix.TransY:F2}]");

                        using var devicePath = new SKPath();
                        glyphPath.Transform(canvasMatrix, devicePath);

                        SKRect deviceBounds = devicePath.Bounds;
                        PdfLogger.Log(LogCategory.Text, $"GLYPH-RENDER: devicePath bounds=({deviceBounds.Left:F2},{deviceBounds.Top:F2},{deviceBounds.Right:F2},{deviceBounds.Bottom:F2})");

                        _canvas.Save();
                        _canvas.ResetMatrix();
                        _canvas.DrawPath(devicePath, paint);
                        _canvas.Restore();
                    }

                    if (shouldStroke)
                    {
                        // Stroke the glyph outline
                        SKColor strokeColor = ConvertColor(state.ResolvedStrokeColor, state.ResolvedStrokeColorSpace);
                        strokeColor = ApplyAlpha(strokeColor, state.StrokeAlpha);

                        double scaledStrokeWidth = state.LineWidth * ctmScale;
                        if (scaledStrokeWidth < 0.5) scaledStrokeWidth = 0.5; // Minimum 0.5 device pixel

                        using var strokePaint = new SKPaint();
                        strokePaint.Color = strokeColor;
                        strokePaint.IsAntialias = true;
                        strokePaint.Style = SKPaintStyle.Stroke;
                        // Use the line width from graphics state for text stroke
                        strokePaint.StrokeWidth = (float)scaledStrokeWidth;
                        _canvas.DrawPath(glyphPath, strokePaint);
                    }
                    else if (applyBold && shouldFill)
                    {
                        // Apply synthetic bold by stroking the glyph outline (only if not already stroking)
                        double scaledBoldWidth = state.FontSize * 0.04 * ctmScale;
                        if (scaledBoldWidth < 0.5) scaledBoldWidth = 0.5; // Minimum 0.5 device pixel

                        using var boldPaint = new SKPaint();
                        boldPaint.Color = paint.Color;
                        boldPaint.IsAntialias = true;
                        boldPaint.Style = SKPaintStyle.Stroke;
                        boldPaint.StrokeWidth = (float)scaledBoldWidth;
                        _canvas.DrawPath(glyphPath, boldPaint);
                    }
                }

                // Clean up path
                glyphPath.Dispose();

                // Advance to the next glyph position
                // NOTE: glyphWidths already include horizontal scaling from PdfRenderer,
                // so we do NOT multiply by tHs again here
                if (i < glyphWidths.Count)
                    currentX += glyphWidths[i];
            }

            return true; // Successfully rendered with glyph outlines
        }
        catch
        {
            // If anything fails, fall back to default rendering
            return false;
        }
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
                    var r = (byte)(colorComponents[0] * 255);
                    var g = (byte)(colorComponents[1] * 255);
                    var b = (byte)(colorComponents[2] * 255);
                    return new SKColor(r, g, b);
                }
                break;
            case "DeviceCMYK":
                if (colorComponents.Count >= 4)
                {
                    // Simple CMYK to RGB conversion
                    double c = colorComponents[0];
                    double m = colorComponents[1];
                    double y = colorComponents[2];
                    double k = colorComponents[3];

                    var r = (byte)((1 - c) * (1 - k) * 255);
                    var g = (byte)((1 - m) * (1 - k) * 255);
                    var b = (byte)((1 - y) * (1 - k) * 255);
                    return new SKColor(r, g, b);
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
        if (path.IsEmpty)
            return;

        _canvas.Save();
        try
        {
            // Apply transformation matrix
            ApplyPathTransformationMatrix(state);

            // Convert IPathBuilder to SKPath
            SKPath skPath = ConvertToSkPath(path);

            // Create stroke paint with alpha from graphics state
            SKColor strokeColor = ConvertColor(state.ResolvedStrokeColor, state.ResolvedStrokeColorSpace);
            strokeColor = ApplyAlpha(strokeColor, state.StrokeAlpha);

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
            SKPath skPath = ConvertToSkPath(path);

            // Set fill rule
            skPath.FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;

            // Create fill paint with alpha from graphics state
            SKColor fillColor = ConvertColor(state.ResolvedFillColor, state.ResolvedFillColorSpace);
            fillColor = ApplyAlpha(fillColor, state.FillAlpha);

            using var paint = new SKPaint();
            paint.Color = fillColor;
            paint.IsAntialias = true;
            paint.Style = SKPaintStyle.Fill;

            _canvas.DrawPath(skPath, paint);
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
            SKPath skPath = ConvertToSkPath(path);
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
            paint.Color = ApplyAlpha(SKColors.White, state.FillAlpha);

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
            SKPath skPath = ConvertToSkPath(path);

            // Set fill rule
            skPath.FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;

            // Fill first (with fill alpha from graphics state)
            SKColor fillColor = ConvertColor(state.ResolvedFillColor, state.ResolvedFillColorSpace);
            fillColor = ApplyAlpha(fillColor, state.FillAlpha);
            using (var fillPaint = new SKPaint())
            {
                fillPaint.Color = fillColor;
                fillPaint.IsAntialias = true;
                fillPaint.Style = SKPaintStyle.Fill;
                _canvas.DrawPath(skPath, fillPaint);
            }

            // Then stroke (with stroke alpha from graphics state)
            SKColor strokeColor = ConvertColor(state.ResolvedStrokeColor, state.ResolvedStrokeColorSpace);
            strokeColor = ApplyAlpha(strokeColor, state.StrokeAlpha);

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
        SKPath skPath = ConvertToSkPath(path);

        // Set fill rule for clipping
        skPath.FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;

        // IMPORTANT: Clipping paths are "frozen" in device coordinates when ClipPath is called.
        // The canvas matrix at the time of clipping transforms the path to device space.
        // We MUST use the exact same _initialTransform that BeginPage uses (including CropBox offset),
        // so that the clip boundary aligns exactly with drawn content.
        //
        // _initialTransform includes:
        // 1. Translate by (-cropOffsetX, -cropOffsetY) - shift so CropBox origin is at (0,0)
        // 2. Scale by (scale, -scale) - scales content and flips Y
        // 3. Translate by (0, height * scale) - moves origin to top-left of scaled output
        var displayMatrix = new SKMatrix(
            _initialTransform.M11, _initialTransform.M21, _initialTransform.M31,
            _initialTransform.M12, _initialTransform.M22, _initialTransform.M32,
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

    // ==================== PATH CONVERSION HELPERS ====================

    /// <summary>
    /// Convert IPathBuilder to SkiaSharp SKPath
    /// </summary>
    private SKPath ConvertToSkPath(IPathBuilder pathBuilder)
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
                    // PDF uses cubic Bézier curves (4 control points)
                    skPath.CubicTo(
                        (float)curveTo.X1, (float)curveTo.Y1,
                        (float)curveTo.X2, (float)curveTo.Y2,
                        (float)curveTo.X3, (float)curveTo.Y3
                    );
                    break;

                case ClosePathSegment:
                    skPath.Close();
                    break;
            }
        }

        return skPath;
    }

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
        // The _initialTransform correctly handles:
        // 1. CropBox offset translation (-cropOffsetX, -cropOffsetY)
        // 2. Y-axis flip with scale
        // 3. Translation to put origin at top-left

        var displayMatrix = new SKMatrix(
            _initialTransform.M11, _initialTransform.M21, _initialTransform.M31,
            _initialTransform.M12, _initialTransform.M22, _initialTransform.M32,
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

    public float MeasureTextWidth(string text, PdfGraphicsState state, PdfFont font)
    {
        if (string.IsNullOrEmpty(text))
            return 0f;

        // Calculate effective font size (same as DrawText)
        // PDF visual font size = FontSize × TextMatrix scaling
        var textMatrixScaleY = (float)Math.Sqrt(state.TextMatrix.M21 * state.TextMatrix.M21 + state.TextMatrix.M22 * state.TextMatrix.M22);
        float effectiveFontSize = (float)state.FontSize * textMatrixScaleY;

        // Determine font style and family (same logic as DrawText)
        SKFontStyle fontStyle = SKFontStyle.Normal;
        var fallbackFontFamily = "Arial"; // Default to sans-serif

        if (font is not null)
        {
            PdfFontDescriptor? descriptor = font.GetDescriptor();
            var isBold = false;
            var isItalic = false;
            var isSerif = false;
            var isMonospace = false;

            if (descriptor is not null)
            {
                // Get font flags
                int flags = descriptor.Flags;
                isSerif = (flags & 0x02) != 0;  // Bit 2: Serif
                isItalic = (flags & 0x40) != 0; // Bit 7: Italic
                isMonospace = (flags & 0x01) != 0; // Bit 1: FixedPitch

                // Check if font is bold from descriptor
                isBold = descriptor.IsBold;

                // Fallback: check BaseFont name for style hints
                string baseName = font.BaseFont;
                if (baseName.Contains("Bold", StringComparison.OrdinalIgnoreCase))
                    isBold = true;
                if (baseName.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
                    baseName.Contains("Oblique", StringComparison.OrdinalIgnoreCase))
                    isItalic = true;
            }

            // Also check font name for common font families
            string baseName2 = font.BaseFont;
            if (baseName2.Contains("Times", StringComparison.OrdinalIgnoreCase) ||
                baseName2.Contains("Garamond", StringComparison.OrdinalIgnoreCase) ||
                baseName2.Contains("Palatino", StringComparison.OrdinalIgnoreCase) ||
                baseName2.Contains("Bookman", StringComparison.OrdinalIgnoreCase))
            {
                isSerif = true;
            }

            // Choose fallback font family based on font classification
            if (isMonospace)
                fallbackFontFamily = "Courier New";
            else if (isSerif)
                fallbackFontFamily = "Times New Roman";
            else
                fallbackFontFamily = "Arial";

            switch (isBold)
            {
                case true when isItalic:
                    fontStyle = SKFontStyle.BoldItalic;
                    break;
                case true:
                    fontStyle = SKFontStyle.Bold;
                    break;
                default:
                {
                    if (isItalic)
                        fontStyle = SKFontStyle.Italic;
                    break;
                }
            }
        }

        // Create font and measure text width
        using var measureFont = new SKFont(SKTypeface.FromFamilyName(fallbackFontFamily, fontStyle), effectiveFontSize);
        float width = measureFont.MeasureText(text);

        // Account for horizontal scaling from graphics state
        // HorizontalScaling is stored as percentage (100 = 100% = 1.0 scale)
        float tHs = (float)state.HorizontalScaling / 100f;
        width *= tHs;

        return width;
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

                        // Use CoreJ2K.ImageSharp's conversion (cross-platform, no Rgb888x bug)
                        Image jp2Image = Jpeg2000.DecompressToImage(rawJp2Data);

                        // Convert ImageSharp Image to SKBitmap
                        SKBitmap jp2Bitmap = ConvertImageSharpToSkBitmap(jp2Image);

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
        GlyphPathCache.Compact(1.0); // Remove all entries
    }

    /// <summary>
    /// Gets the current number of cached glyph paths (approximate)
    /// </summary>
    public static int GetCacheCount()
    {
        return GlyphPathCache.Count;
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

    public void BeginPage(int pageNumber, double width, double height, double scale = 1.0, double cropOffsetX = 0, double cropOffsetY = 0)
    {
        // Pattern content uses pattern space coordinates, no additional transform needed
        // The canvas transformation is already set up by the caller
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
                    var r = (byte)(255 * (1 - c) * (1 - k));
                    var g = (byte)(255 * (1 - m) * (1 - k));
                    var b = (byte)(255 * (1 - y) * (1 - k));
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
                            int c = imageData[srcOffset];
                            int m = imageData[srcOffset + 1];
                            int y = imageData[srcOffset + 2];
                            int k = imageData[srcOffset + 3];
                            pixelBuffer[dstOffset] = (byte)((255 - c) * (255 - k) / 255);
                            pixelBuffer[dstOffset + 1] = (byte)((255 - m) * (255 - k) / 255);
                            pixelBuffer[dstOffset + 2] = (byte)((255 - y) * (255 - k) / 255);
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
