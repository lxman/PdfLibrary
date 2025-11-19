using System.Numerics;
using Microsoft.Extensions.Caching.Memory;
using PdfLibrary.Content;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Structure;
using SkiaSharp;

namespace PdfLibrary.Rendering;

/// <summary>
/// SkiaSharp-based render target for pixel-perfect PDF rendering.
/// Uses embedded font glyph outlines for text rendering.
/// </summary>
public class SkiaSharpRenderTarget : IRenderTarget
{
    private readonly SKCanvas _canvas;
    private readonly SKSurface _surface;
    private readonly GlyphToSKPathConverter _glyphConverter;
    private readonly Stack<SKMatrix> _stateStack;
    private readonly PdfDocument? _document;
    private double _pageWidth;
    private double _pageHeight;

    // Static glyph path cache - shared across all render targets for efficiency
    private static readonly MemoryCache _glyphPathCache;
    private static readonly MemoryCacheEntryOptions _cacheOptions;

    public int CurrentPageNumber { get; private set; }

    static SkiaSharpRenderTarget()
    {
        // Initialize cache with size limit (approx 10,000 glyphs)
        var cacheOptions = new MemoryCacheOptions
        {
            SizeLimit = 10000
        };
        _glyphPathCache = new MemoryCache(cacheOptions);

        // Cache entries expire after 10 minutes of non-use
        _cacheOptions = new MemoryCacheEntryOptions()
            .SetSize(1)
            .SetSlidingExpiration(TimeSpan.FromMinutes(10))
            .RegisterPostEvictionCallback((key, value, reason, state) =>
            {
                // Dispose SKPath when evicted from cache
                if (value is SKPath path)
                    path.Dispose();
            });
    }

    public SkiaSharpRenderTarget(int width, int height, PdfDocument? document = null)
    {
        _document = document;

        // Create SkiaSharp surface
        var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        _surface = SKSurface.Create(imageInfo);
        _canvas = _surface.Canvas;

        _glyphConverter = new GlyphToSKPathConverter();
        _stateStack = new Stack<SKMatrix>();

        // Clear to white background
        _canvas.Clear(SKColors.White);
    }

    // ==================== PAGE LIFECYCLE ====================

    public void BeginPage(int pageNumber, double width, double height)
    {
        CurrentPageNumber = pageNumber;
        _pageWidth = width;
        _pageHeight = height;

        // Clear canvas for new page
        _canvas.Clear(SKColors.White);

        // Reset to identity matrix - we'll handle PDF-to-device coordinate
        // transformation per object, following PDFium's matrix-based approach
        _canvas.ResetMatrix();
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
    }

    // ==================== STATE MANAGEMENT ====================

    public void SaveState()
    {
        _canvas.Save();
        _stateStack.Push(_canvas.TotalMatrix);
    }

    public void RestoreState()
    {
        _canvas.Restore();
        if (_stateStack.Count > 0)
            _stateStack.Pop();
    }

    // ==================== TEXT RENDERING ====================

    public void DrawText(string text, List<double> glyphWidths, PdfGraphicsState state, PdfFont? font, List<int>? charCodes = null)
    {
        if (string.IsNullOrEmpty(text) || state.FontName == null)
            return;

        _canvas.Save();

        try
        {
            // Apply transformation matrix with Y-axis conversion
            // Following PDFium's approach: convert PDF space to device space
            ApplyTransformationMatrix(state);

            // Convert fill color
            var fillColor = ConvertColor(state.FillColor, state.FillColorSpace);

            // Debug: show text color if it's not black
            if (fillColor.Red != 0 || fillColor.Green != 0 || fillColor.Blue != 0)
            {
                Console.WriteLine($"[TEXT COLOR] Text='{(text.Length > 20 ? text.Substring(0, 20) + "..." : text)}' Color=RGB({fillColor.Red},{fillColor.Green},{fillColor.Blue}) ColorSpace={state.FillColorSpace}");
            }

            using var paint = new SKPaint
            {
                Color = fillColor,
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

            // Try to render using embedded font glyph outlines
            if (font != null && TryRenderWithGlyphOutlines(text, glyphWidths, state, font, paint, charCodes))
            {
                // Successfully rendered with glyph outlines
                return;
            }

            // Fallback: render each character individually using PDF glyph widths
            // This preserves correct spacing even when using a substitute font

            // Determine font style from font descriptor
            var fontStyle = SKFontStyle.Normal;
            if (font != null)
            {
                var descriptor = font.GetDescriptor();
                bool isBold = false;
                bool isItalic = false;

                if (descriptor != null)
                {
                    isBold = descriptor.IsBold;
                    isItalic = descriptor.IsItalic;

                    // Also use StemV as heuristic for bold detection
                    if (descriptor.StemV >= 120)
                        isBold = true;
                }

                // Also check font name for style hints
                string? baseName = font.BaseFont;
                if (baseName != null)
                {
                    if (baseName.Contains("Bold", StringComparison.OrdinalIgnoreCase))
                        isBold = true;
                    if (baseName.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
                        baseName.Contains("Oblique", StringComparison.OrdinalIgnoreCase))
                        isItalic = true;
                }

                if (isBold && isItalic)
                    fontStyle = SKFontStyle.BoldItalic;
                else if (isBold)
                    fontStyle = SKFontStyle.Bold;
                else if (isItalic)
                    fontStyle = SKFontStyle.Italic;
            }

            using var fallbackFont = new SKFont(SKTypeface.FromFamilyName("Arial", fontStyle), (float)state.FontSize);

            float currentX = 0;
            for (int i = 0; i < text.Length; i++)
            {
                string ch = text[i].ToString();
                _canvas.DrawText(ch, currentX, 0, fallbackFont, paint);

                // Advance by PDF glyph width, not Arial's width
                if (i < glyphWidths.Count)
                    currentX += (float)glyphWidths[i];
            }
        }
        finally
        {
            _canvas.Restore();
        }
    }

    private bool TryRenderWithGlyphOutlines(string text, List<double> glyphWidths, PdfGraphicsState state, PdfFont font, SKPaint paint, List<int>? charCodes)
    {
        try
        {
            // Check if font should be rendered as bold (for synthetic bold)
            bool applyBold = false;
            var descriptor = font.GetDescriptor();
            if (descriptor != null)
            {
                // Check font flags for bold, or check font name for "Bold"
                bool isBoldFlag = descriptor.IsBold;
                bool isBoldName = font.BaseFont?.Contains("Bold", StringComparison.OrdinalIgnoreCase) == true;

                // Also use StemV as heuristic - higher values indicate bolder fonts
                // Regular fonts typically have StemV < 100, bold fonts > 150
                // Values 120-150 suggest semi-bold or bold variants without "Bold" in name
                bool isBoldStemV = descriptor.StemV >= 120;

                applyBold = isBoldFlag || isBoldName || isBoldStemV;
            }

            // Get embedded font metrics
            var embeddedMetrics = font.GetEmbeddedMetrics();
            if (embeddedMetrics == null || !embeddedMetrics.IsValid)
                return false;

            // Position for rendering glyphs
            double currentX = 0;

            // Iterate over character codes, not Unicode characters
            // One character code can decode to multiple Unicode chars (e.g., ligatures)
            int loopCount = charCodes?.Count ?? text.Length;

            for (int i = 0; i < loopCount; i++)
            {
                // Get character code - either from original PDF codes or fall back to Unicode
                ushort charCode = charCodes != null && i < charCodes.Count
                    ? (ushort)charCodes[i]
                    : (ushort)text[i];

                // Get corresponding character for logging (may not match 1:1 due to ligatures)
                char displayChar = i < text.Length ? text[i] : '?';

                ushort glyphId;

                // For CFF fonts without cmap, use glyph name mapping via the PDF encoding
                if (embeddedMetrics.IsCffFont && font.Encoding != null)
                {
                    string? glyphName = font.Encoding.GetGlyphName(charCode);
                    if (glyphName != null)
                    {
                        glyphId = embeddedMetrics.GetGlyphIdByName(glyphName);
                    }
                    else
                    {
                        // Fall back to direct lookup
                        glyphId = embeddedMetrics.GetGlyphId(charCode);
                    }
                }
                else
                {
                    // For Type0/CID fonts, map CID to GID using CIDToGIDMap
                    if (font is Type0Font type0Font && type0Font.DescendantFont is CidFont cidFont)
                    {
                        // For Type0 fonts, use CIDToGIDMap directly - the mapped value IS the glyph ID
                        int mappedGid = cidFont.MapCidToGid(charCode);
                        glyphId = (ushort)mappedGid;
                    }
                    else
                    {
                        // For other fonts, use cmap lookup
                        glyphId = embeddedMetrics.GetGlyphId(charCode);
                    }
                }

                if (glyphId == 0)
                {
                    // Glyph not found, skip this character
                    if (i < glyphWidths.Count)
                        currentX += glyphWidths[i];
                    continue;
                }

                // Extract glyph outline
                var glyphOutline = embeddedMetrics.GetGlyphOutline(glyphId);
                if (glyphOutline == null)
                {
                    if (i < glyphWidths.Count)
                        currentX += glyphWidths[i];
                    continue;
                }

                if (glyphOutline.IsEmpty)
                {
                    // Empty glyph (e.g., space), just advance
                    if (i < glyphWidths.Count)
                        currentX += glyphWidths[i];
                    continue;
                }

                // Create cache key: font name + glyph ID + font size (rounded for precision)
                string fontKey = font.BaseFont ?? "unknown";
                int roundedSize = (int)(state.FontSize * 10); // 0.1pt precision
                string cacheKey = $"{fontKey}_{glyphId}_{roundedSize}";

                // Try to get from cache first
                SKPath glyphPath;
                if (_glyphPathCache.TryGetValue(cacheKey, out SKPath? cachedPath) && cachedPath != null)
                {
                    // Clone the cached path (we need to transform it)
                    glyphPath = new SKPath(cachedPath);
                }
                else
                {
                    // Convert glyph outline to SKPath
                    // Check if this is a CFF font for proper cubic Bezier rendering
                    if (embeddedMetrics.IsCffFont)
                    {
                        var cffOutline = embeddedMetrics.GetCffGlyphOutlineDirect(glyphId);
                        if (cffOutline != null)
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
                    _glyphPathCache.Set(cacheKey, pathToCache, _cacheOptions);
                }

                // Translate path to current position
                var matrix = SKMatrix.CreateTranslation((float)currentX, 0);
                glyphPath.Transform(matrix);

                // Render the glyph
                _canvas.DrawPath(glyphPath, paint);

                // Apply synthetic bold by stroking the glyph outline
                if (applyBold)
                {
                    // Calculate CTM scaling factor for stroke width
                    // Note: Since canvas already has CTM transform, this may cause double-scaling
                    // But we apply it for consistency with line width handling
                    var ctm = state.Ctm;
                    double ctmScale = Math.Sqrt(Math.Abs(ctm.M11 * ctm.M22 - ctm.M12 * ctm.M21));
                    if (ctmScale < 0.0001) ctmScale = 1.0;

                    using var strokePaint = new SKPaint
                    {
                        Color = paint.Color,
                        IsAntialias = true,
                        Style = SKPaintStyle.Stroke,
                        StrokeWidth = (float)(state.FontSize * 0.04 * ctmScale)
                    };
                    _canvas.DrawPath(glyphPath, strokePaint);
                }

                // Clean up path
                glyphPath.Dispose();

                // Advance to next glyph position
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
        // Combine CTM and text matrix (both in PDF space)
        var pdfMatrix = state.Ctm * state.TextMatrix;

        // Create page-to-device display matrix following PDFium's approach
        // For rotation=0: [1, 0, 0, -1, 0, pageHeight]
        // This maps PDF coordinates (x, y) to device coordinates (x, pageHeight - y)
        var displayMatrix = new Matrix3x2(
            1, 0,                       // a, b
            0, -1,                      // c, d (negative d flips Y-axis)
            0, (float)_pageHeight       // e, f (translate Y by page height)
        );

        // Concatenate transformations: pdfMatrix * displayMatrix
        var deviceMatrix = pdfMatrix * displayMatrix;

        // Convert to SKMatrix
        // Note: We negate ScaleY to counter-flip the glyphs (they were flipped by displayMatrix)
        // This keeps the text position correct (from the matrix multiplication)
        // but renders glyphs right-side-up
        var skMatrix = new SKMatrix
        {
            ScaleX = deviceMatrix.M11,
            SkewY = deviceMatrix.M12,
            SkewX = deviceMatrix.M21,
            ScaleY = -deviceMatrix.M22,  // Negate to counter-flip glyphs
            TransX = deviceMatrix.M31,
            TransY = deviceMatrix.M32,
            Persp0 = 0,
            Persp1 = 0,
            Persp2 = 1
        };

        _canvas.Concat(in skMatrix);
    }

    private SKColor ConvertColor(List<double> colorComponents, string colorSpace)
    {
        if (colorComponents == null || colorComponents.Count == 0)
        {
            Console.WriteLine($"[COLOR] Warning: No color components for colorSpace={colorSpace}");
            return SKColors.Black;
        }

        // Log non-standard color spaces
        if (colorSpace != "DeviceGray" && colorSpace != "DeviceRGB" && colorSpace != "DeviceCMYK")
        {
            Console.WriteLine($"[COLOR] Non-device colorSpace={colorSpace}, components=[{string.Join(", ", colorComponents)}]");
        }

        switch (colorSpace)
        {
            case "DeviceGray":
                {
                    byte gray = (byte)(colorComponents[0] * 255);
                    return new SKColor(gray, gray, gray);
                }
            case "DeviceRGB":
                if (colorComponents.Count >= 3)
                {
                    byte r = (byte)(colorComponents[0] * 255);
                    byte g = (byte)(colorComponents[1] * 255);
                    byte b = (byte)(colorComponents[2] * 255);
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

                    byte r = (byte)((1 - c) * (1 - k) * 255);
                    byte g = (byte)((1 - m) * (1 - k) * 255);
                    byte b = (byte)((1 - y) * (1 - k) * 255);
                    return new SKColor(r, g, b);
                }
                break;
            default:
                // For named/unknown color spaces, try to interpret based on component count
                // This is a fallback - proper implementation would resolve the named color space
                if (colorComponents.Count >= 4)
                {
                    // Treat as CMYK
                    double c = colorComponents[0];
                    double m = colorComponents[1];
                    double y = colorComponents[2];
                    double k = colorComponents[3];
                    byte r = (byte)((1 - c) * (1 - k) * 255);
                    byte g = (byte)((1 - m) * (1 - k) * 255);
                    byte b = (byte)((1 - y) * (1 - k) * 255);
                    return new SKColor(r, g, b);
                }
                else if (colorComponents.Count >= 3)
                {
                    // Treat as RGB
                    byte r = (byte)(colorComponents[0] * 255);
                    byte g = (byte)(colorComponents[1] * 255);
                    byte b = (byte)(colorComponents[2] * 255);
                    return new SKColor(r, g, b);
                }
                else if (colorComponents.Count >= 1)
                {
                    // Treat as grayscale
                    byte gray = (byte)(colorComponents[0] * 255);
                    return new SKColor(gray, gray, gray);
                }
                break;
        }

        return SKColors.Black;
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
            var skPath = ConvertToSKPath(path);

            // Create stroke paint
            var strokeColor = ConvertColor(state.StrokeColor, state.StrokeColorSpace);


            // Calculate CTM scaling factor for line width
            // The line width should be scaled by the CTM's linear scaling factor
            // which is the square root of the absolute determinant of the 2x2 portion
            var ctm = state.Ctm;
            double ctmScale = Math.Sqrt(Math.Abs(ctm.M11 * ctm.M22 - ctm.M12 * ctm.M21));
            if (ctmScale < 0.0001) ctmScale = 1.0; // Avoid division by zero
            double scaledLineWidth = state.LineWidth * ctmScale;

            using var paint = new SKPaint
            {
                Color = strokeColor,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)scaledLineWidth,
                StrokeCap = ConvertLineCap(state.LineCap),
                StrokeJoin = ConvertLineJoin(state.LineJoin),
                StrokeMiter = (float)state.MiterLimit
            };

            // Apply dash pattern if present (scale by CTM as well)
            if (state.DashPattern != null && state.DashPattern.Length > 0)
            {
                var dashIntervals = state.DashPattern.Select(d => (float)(d * ctmScale)).ToArray();
                Console.WriteLine($"[DASH] Applying dash pattern: [{string.Join(", ", dashIntervals)}] phase={state.DashPhase * ctmScale}");
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
            var skPath = ConvertToSKPath(path);

            // Set fill rule
            skPath.FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;

            // Create fill paint
            var fillColor = ConvertColor(state.FillColor, state.FillColorSpace);
            using var paint = new SKPaint
            {
                Color = fillColor,
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

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
            var skPath = ConvertToSKPath(path);

            // Set fill rule
            skPath.FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;

            // Fill first
            var fillColor = ConvertColor(state.FillColor, state.FillColorSpace);
            using (var fillPaint = new SKPaint
            {
                Color = fillColor,
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            })
            {
                _canvas.DrawPath(skPath, fillPaint);
            }

            // Then stroke
            var strokeColor = ConvertColor(state.StrokeColor, state.StrokeColorSpace);

            // Calculate CTM scaling factor for line width
            var ctm = state.Ctm;
            double ctmScale = Math.Sqrt(Math.Abs(ctm.M11 * ctm.M22 - ctm.M12 * ctm.M21));
            if (ctmScale < 0.0001) ctmScale = 1.0;
            double scaledLineWidth = state.LineWidth * ctmScale;

            using (var strokePaint = new SKPaint
            {
                Color = strokeColor,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)scaledLineWidth,
                StrokeCap = ConvertLineCap(state.LineCap),
                StrokeJoin = ConvertLineJoin(state.LineJoin),
                StrokeMiter = (float)state.MiterLimit
            })
            {
                // Apply dash pattern if present (scale by CTM as well)
                if (state.DashPattern != null && state.DashPattern.Length > 0)
                {
                    var dashIntervals = state.DashPattern.Select(d => (float)(d * ctmScale)).ToArray();
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

        // Convert IPathBuilder to SKPath in PDF coordinates
        var skPath = ConvertToSKPath(path);

        // Set fill rule for clipping
        skPath.FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;

        // Transform the path to device coordinates
        // Only apply Y-flip - the CTM was already applied when the path was built
        var flipMatrix = new SKMatrix
        {
            ScaleX = 1,
            SkewY = 0,
            SkewX = 0,
            ScaleY = -1,
            TransX = 0,
            TransY = (float)_pageHeight,
            Persp0 = 0,
            Persp1 = 0,
            Persp2 = 1
        };

        skPath.Transform(flipMatrix);

        // Apply clipping path (persists until RestoreState is called)
        _canvas.ClipPath(skPath, SKClipOperation.Intersect, antialias: true);
        skPath.Dispose();
    }

    // ==================== PATH CONVERSION HELPERS ====================

    /// <summary>
    /// Convert IPathBuilder to SkiaSharp SKPath
    /// </summary>
    private SKPath ConvertToSKPath(IPathBuilder pathBuilder)
    {
        var skPath = new SKPath();

        if (pathBuilder is not PathBuilder builder)
            return skPath;

        foreach (var segment in builder.Segments)
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
    /// Paths are already transformed by CTM during construction (OnMoveTo, OnLineTo, etc.)
    /// So we only need to apply the display matrix (Y-flip) here
    /// </summary>
    private void ApplyPathTransformationMatrix(PdfGraphicsState state)
    {
        // Paths are already transformed by CTM during construction
        // Only apply the display matrix (Y-flip for PDF to screen coordinates)
        var displayMatrix = new Matrix3x2(
            1, 0,
            0, -1,
            0, (float)_pageHeight
        );

        // Convert to SKMatrix
        var skMatrix = new SKMatrix
        {
            ScaleX = displayMatrix.M11,
            SkewY = displayMatrix.M12,
            SkewX = displayMatrix.M21,
            ScaleY = displayMatrix.M22,
            TransX = displayMatrix.M31,
            TransY = displayMatrix.M32,
            Persp0 = 0,
            Persp1 = 0,
            Persp2 = 1
        };

        _canvas.Concat(in skMatrix);
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

    public void DrawImage(PdfImage image, PdfGraphicsState state)
    {
        try
        {
            // Create SKBitmap from PDF image data
            // For image masks, pass the fill color to use as the stencil color
            SKColor? fillColor = null;
            if (image.IsImageMask)
            {
                fillColor = ConvertColor(state.FillColor, state.FillColorSpace);
            }
            var bitmap = CreateBitmapFromPdfImage(image, fillColor);
            if (bitmap == null)
                return;

            _canvas.Save();

            try
            {
                // Get CTM values - this defines how the unit square maps to PDF page coordinates
                var pdfMatrix = state.Ctm;

                // Create transformation matrix for device coordinates
                // Apply CTM first, then flip Y for device space
                var ctmMatrix = new SKMatrix
                {
                    ScaleX = pdfMatrix.M11,
                    SkewY = pdfMatrix.M12,
                    SkewX = pdfMatrix.M21,
                    ScaleY = pdfMatrix.M22,
                    TransX = pdfMatrix.M31,
                    TransY = pdfMatrix.M32,
                    Persp0 = 0,
                    Persp1 = 0,
                    Persp2 = 1
                };

                // Y-flip matrix for device coordinates
                var flipMatrix = new SKMatrix
                {
                    ScaleX = 1,
                    SkewY = 0,
                    SkewX = 0,
                    ScaleY = -1,
                    TransX = 0,
                    TransY = (float)_pageHeight,
                    Persp0 = 0,
                    Persp1 = 0,
                    Persp2 = 1
                };

                // Concatenate: CTM * flip (apply CTM first, then flip for device coords)
                var deviceMatrix = ctmMatrix.PostConcat(flipMatrix);
                _canvas.Concat(deviceMatrix);

                // Draw image in canonical 1x1 unit square at origin
                // The transformation matrix will position/scale/rotate it correctly
                var unitRect = new SKRect(0, 0, 1, 1);

                // Use high-quality sampling for image scaling
                using var paint = new SKPaint
                {
                    IsAntialias = true,
                    FilterQuality = SKFilterQuality.High
                };

                // Draw bitmap with explicit source and destination rects
                var srcRect = new SKRect(0, 0, bitmap.Width, bitmap.Height);
                _canvas.DrawBitmap(bitmap, srcRect, unitRect, paint);
            }
            finally
            {
                _canvas.Restore();
                bitmap.Dispose();
            }
        }
        catch
        {
            // Image rendering failed, skip this image
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

            // Check for SMask (alpha channel / transparency)
            byte[]? smaskData = null;
            bool hasSMask = image.HasAlpha;
            if (hasSMask)
            {
                // Extract SMask from the image stream
                var stream = image.Stream;
                if (stream.Dictionary.TryGetValue(new PdfName("SMask"), out var smaskObj))
                {
                    // Resolve indirect reference if needed
                    if (smaskObj is PdfIndirectReference smaskRef && _document != null)
                        smaskObj = _document.ResolveReference(smaskRef);

                    if (smaskObj is PdfStream smaskStream)
                    {
                        var smaskImage = new PdfImage(smaskStream, _document);
                        smaskData = smaskImage.GetDecodedData();
                    }
                }
            }

            // Determine SkiaSharp color type based on PDF color space
            SKBitmap? bitmap = null;

            // Handle image masks (1-bit stencil images)
            if (image.IsImageMask && imageMaskColor.HasValue)
            {
                // Image masks are 1-bit images where painted pixels use the fill color
                bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                var color = imageMaskColor.Value;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        // PDF images are stored bottom-up, flip Y for top-down bitmap
                        int srcY = height - 1 - y;

                        // Calculate bit position
                        int bitIndex = srcY * width + x;
                        int byteIndex = bitIndex / 8;
                        int bitOffset = 7 - (bitIndex % 8); // MSB first

                        if (byteIndex >= imageData.Length)
                            continue;

                        // Get the mask bit (1 = paint, 0 = transparent by default)
                        // Note: The Decode array can invert this, but default is [0 1]
                        bool paint = ((imageData[byteIndex] >> bitOffset) & 1) == 1;

                        if (paint)
                        {
                            bitmap.SetPixel(x, y, color);
                        }
                        else
                        {
                            bitmap.SetPixel(x, y, SKColors.Transparent);
                        }
                    }
                }

                return bitmap;
            }

            if (colorSpace == "Indexed")
            {
                // Handle indexed color images
                var paletteData = image.GetIndexedPalette(out string? baseColorSpace, out int hival);
                if (paletteData == null || baseColorSpace == null)
                    return null;

                // Use Unpremul alpha type for SMask (we set colors without premultiplying)
                var alphaType = hasSMask ? SKAlphaType.Unpremul : SKAlphaType.Opaque;
                bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, alphaType);

                int componentsPerEntry = baseColorSpace switch
                {
                    "DeviceRGB" => 3,
                    "DeviceGray" => 1,
                    _ => 3
                };

                // Convert indexed pixels to RGBA
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        // PDF images are stored bottom-up, flip Y for top-down bitmap
                        int pixelIndex = (height - 1 - y) * width + x;
                        if (pixelIndex >= imageData.Length)
                            continue;

                        byte paletteIndex = imageData[pixelIndex];
                        if (paletteIndex > hival)
                            paletteIndex = (byte)hival;

                        int paletteOffset = paletteIndex * componentsPerEntry;

                        SKColor color;
                        if (componentsPerEntry == 3 && paletteOffset + 2 < paletteData.Length)
                        {
                            byte r = paletteData[paletteOffset];
                            byte g = paletteData[paletteOffset + 1];
                            byte b = paletteData[paletteOffset + 2];

                            // Apply SMask alpha channel if present
                            byte alpha = 255;
                            if (smaskData != null && pixelIndex < smaskData.Length)
                            {
                                alpha = smaskData[pixelIndex];
                            }

                            color = new SKColor(r, g, b, alpha);
                        }
                        else if (componentsPerEntry == 1 && paletteOffset < paletteData.Length)
                        {
                            byte gray = paletteData[paletteOffset];

                            // Apply SMask alpha channel if present
                            byte alpha = 255;
                            if (smaskData != null && pixelIndex < smaskData.Length)
                            {
                                alpha = smaskData[pixelIndex];
                            }

                            color = new SKColor(gray, gray, gray, alpha);
                        }
                        else
                        {
                            color = SKColors.Black;
                        }

                        bitmap.SetPixel(x, y, color);
                    }
                }
            }
            else if ((colorSpace == "DeviceRGB" || colorSpace == "CalRGB") && bitsPerComponent == 8)
            {
                var alphaType = hasSMask ? SKAlphaType.Unpremul : SKAlphaType.Opaque;
                bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, alphaType);
                int expectedSize = width * height * 3;
                if (imageData.Length < expectedSize)
                    return null;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        // PDF images are stored bottom-up, flip Y for top-down bitmap
                        int pixelIndex = (height - 1 - y) * width + x;
                        int offset = pixelIndex * 3;
                        byte r = imageData[offset];
                        byte g = imageData[offset + 1];
                        byte b = imageData[offset + 2];

                        // Apply SMask alpha channel if present
                        byte alpha = 255;
                        if (smaskData != null)
                        {
                            if (pixelIndex < smaskData.Length)
                                alpha = smaskData[pixelIndex];
                        }

                        bitmap.SetPixel(x, y, new SKColor(r, g, b, alpha));
                    }
                }
            }
            else if ((colorSpace == "DeviceGray" || colorSpace == "CalGray") && bitsPerComponent == 8)
            {
                var alphaType = hasSMask ? SKAlphaType.Unpremul : SKAlphaType.Opaque;
                bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, alphaType);
                int expectedSize = width * height;
                if (imageData.Length < expectedSize)
                    return null;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        // PDF images are stored bottom-up, flip Y for top-down bitmap
                        int pixelIndex = (height - 1 - y) * width + x;
                        byte gray = imageData[pixelIndex];

                        // Apply SMask alpha channel if present
                        byte alpha = 255;
                        if (smaskData != null && pixelIndex < smaskData.Length)
                            alpha = smaskData[pixelIndex];

                        bitmap.SetPixel(x, y, new SKColor(gray, gray, gray, alpha));
                    }
                }
            }
            else
            {
                // Unsupported color space/bits per component combination
                return null;
            }

            return bitmap;
        }
        catch
        {
            return null;
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
        using var image = _surface.Snapshot();
        using var data = image.Encode(format, quality);
        using var stream = File.OpenWrite(filePath);
        data.SaveTo(stream);
    }

    public void Dispose()
    {
        _surface?.Dispose();
    }

    /// <summary>
    /// Clears the glyph path cache to free memory
    /// </summary>
    public static void ClearGlyphCache()
    {
        _glyphPathCache.Compact(1.0); // Remove all entries
    }

    /// <summary>
    /// Gets the current number of cached glyph paths (approximate)
    /// </summary>
    public static int GetCacheCount()
    {
        return _glyphPathCache.Count;
    }
}
