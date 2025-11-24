using System.Numerics;
using Microsoft.Extensions.Caching.Memory;
using PdfLibrary.Content;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using Logging;
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
    private Matrix3x2 _initialTransform = Matrix3x2.Identity;

    // Static glyph path cache - shared across all render targets for efficiency
    private static readonly MemoryCache GlyphPathCache;
    private static readonly MemoryCacheEntryOptions CacheOptions;

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

        PdfLogger.Log(LogCategory.Transforms, $"BeginPage: Page {pageNumber}, Size: {width}x{height}");

        // Clear canvas for the new page
        _canvas.Clear(SKColors.White);

        // Set up initial viewport transformation:
        // PDF coordinate system has origin at bottom-left with Y increasing upward
        // The screen coordinate system has origin at top-left with Y increasing downward
        // So we need to flip the Y-axis
        Matrix3x2 initialTransform = Matrix3x2.CreateScale(1, -1) * Matrix3x2.CreateTranslation(0, (float)height);

        PdfLogger.Log(LogCategory.Transforms, $"Initial viewport transform: Scale(1,-1) × Translate(0,{height})");

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

    public void ApplyCtm(Matrix3x2 ctm)
    {
        // The CTM parameter is the FULL accumulated transformation matrix from PdfGraphicsState.
        // We need to combine it with our initial viewport transformation.
        //
        // Matrix multiplication order: rightmost matrix is applied first.
        // We want InitialTransform applied first, then CTM, so: CTM × InitialTransform

        PdfLogger.Log(LogCategory.Transforms, $"ApplyCtm: PDF CTM=[{ctm.M11:F4}, {ctm.M12:F4}, {ctm.M21:F4}, {ctm.M22:F4}, {ctm.M31:F4}, {ctm.M32:F4}]");

        // Combine: CTM is applied to the viewport-transformed coordinates
        Matrix3x2 finalTransform = ctm * _initialTransform;

        // Convert to SKMatrix
        // Matrix3x2 to SKMatrix mapping: M11→scaleX, M21→skewX, M31→transX, M12→skewY, M22→scaleY, M32→transY
        var finalMatrix = new SKMatrix(
            finalTransform.M11, finalTransform.M21, finalTransform.M31,  // scaleX, skewX, transX
            finalTransform.M12, finalTransform.M22, finalTransform.M32,  // skewY, scaleY, transY
            0, 0, 1
        );

        PdfLogger.Log(LogCategory.Transforms, $"Final canvas matrix=[{finalMatrix.ScaleX:F4}, {finalMatrix.SkewY:F4}, {finalMatrix.SkewX:F4}, {finalMatrix.ScaleY:F4}, {finalMatrix.TransX:F4}, {finalMatrix.TransY:F4}]");

        _canvas.SetMatrix(finalMatrix);
    }

    // ==================== TEXT RENDERING ====================

    public void DrawText(string text, List<double> glyphWidths, PdfGraphicsState state, PdfFont? font, List<int>? charCodes = null)
    {
        if (string.IsNullOrEmpty(text) || state.FontName == null)
            return;

        _canvas.Save();

        try
        {
            // Note: We no longer apply glyph transformation to the canvas here.
            // Instead, each glyph path is transformed individually with the full glyph matrix.
            // Canvas already has (displayMatrix × CTM) from ApplyCtm().

            // Convert fill color
            SKColor fillColor = ConvertColor(state.FillColor, state.FillColorSpace);

            // Debug: show text color if it's not black
            if (fillColor.Red != 0 || fillColor.Green != 0 || fillColor.Blue != 0)
            {
                PdfLogger.Log(LogCategory.Text, $"TEXT COLOR: Text='{(text.Length > 20 ? text[..20] + "..." : text)}' Color=RGB({fillColor.Red},{fillColor.Green},{fillColor.Blue}) ColorSpace={state.FillColorSpace}");
            }

            using var paint = new SKPaint();
            paint.Color = fillColor;
            paint.IsAntialias = true;
            paint.Style = SKPaintStyle.Fill;

            // Try to render using embedded font glyph outlines
            if (font is not null && TryRenderWithGlyphOutlines(text, glyphWidths, state, font, paint, charCodes))
            {
                // Successfully rendered with glyph outlines
                PdfLogger.Log(LogCategory.Text, $"Glyph outline rendering succeeded for '{(text.Length > 20 ? text[..20] + "..." : text)}'");
                return;
            }

            PdfLogger.Log(LogCategory.Text, $"Falling back to Arial for '{(text.Length > 20 ? text[..20] + "..." : text)}'");

            // Fallback: render each character individually using PDF glyph widths
            // This preserves correct spacing even when using a substitute font

            // For fallback rendering with SKFont, the font size is already applied to the font
            // So we only need to apply TextMatrix and TextRise (NOT font size scaling)
            // Note: We still need horizontal scaling
            // HorizontalScaling is stored as a percentage (100 = 100% = 1.0 scale)
            float tHs = (float)state.HorizontalScaling / 100f;
            var tRise = (float)state.TextRise;

            // Apply only horizontal scaling and text rise, not font size
            // (SKFont already has the font size baked in)
            var textPositionMatrix = new Matrix3x2(
                tHs, 0,       // Horizontal scaling only
                0, 1,         // No Y scaling (font already sized)
                0, tRise      // Text rise
            );

            Matrix3x2 fallbackMatrix = textPositionMatrix * state.TextMatrix;

            PdfLogger.Log(LogCategory.Text, $"FALLBACK: FontSize={state.FontSize:F2}, HScale={tHs:F2}, Rise={tRise:F2}");
            PdfLogger.Log(LogCategory.Text, $"  TextMatrix=[{state.TextMatrix.M11:F4}, {state.TextMatrix.M12:F4}, {state.TextMatrix.M21:F4}, {state.TextMatrix.M22:F4}, {state.TextMatrix.M31:F4}, {state.TextMatrix.M32:F4}]");
            PdfLogger.Log(LogCategory.Text, $"  FallbackMatrix (no FontSize)=[{fallbackMatrix.M11:F4}, {fallbackMatrix.M12:F4}, {fallbackMatrix.M21:F4}, {fallbackMatrix.M22:F4}, {fallbackMatrix.M31:F4}, {fallbackMatrix.M32:F4}]");

            // Determine font style from font descriptor
            SKFontStyle? fontStyle = SKFontStyle.Normal;
            if (font is not null)
            {
                PdfFontDescriptor? descriptor = font.GetDescriptor();
                var isBold = false;
                var isItalic = false;

                if (descriptor is not null)
                {
                    isBold = descriptor.IsBold;
                    isItalic = descriptor.IsItalic;

                    // Also use StemV as a heuristic for bold detection
                    if (descriptor.StemV >= 120)
                        isBold = true;
                }

                // Also check the font name for style hints
                string baseName = font.BaseFont;
                if (baseName.Contains("Bold", StringComparison.OrdinalIgnoreCase))
                    isBold = true;
                if (baseName.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
                    baseName.Contains("Oblique", StringComparison.OrdinalIgnoreCase))
                    isItalic = true;

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

            using var fallbackFont = new SKFont(SKTypeface.FromFamilyName("Arial", fontStyle), (float)state.FontSize);

            float currentX = 0;
            for (var i = 0; i < text.Length; i++)
            {
                // Transform character position through fallback matrix
                // (excludes font size since SKFont already has it)
                Vector2 position = Vector2.Transform(new Vector2(currentX, 0), fallbackMatrix);

                PdfLogger.Log(LogCategory.Text, $"DRAW CHAR: '{text[i]}' currentX={currentX:F4} → position=({position.X:F4}, {position.Y:F4})");

                var ch = text[i].ToString();

                // The canvas has a Y-flip applied, which makes text render upside down
                // We need to apply a local Y-flip at the text position to counter this
                _canvas.Save();
                _canvas.Translate(position.X, position.Y);
                _canvas.Scale(1, -1);  // Flip Y to make text right-side up
                _canvas.DrawText(ch, 0, 0, fallbackFont, paint);
                _canvas.Restore();

                // Advance by PDF glyph width, scaled by horizontal scaling
                if (i < glyphWidths.Count)
                    currentX += (float)glyphWidths[i] * tHs;
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
            // Check if the font should be rendered as bold (for synthetic bold)
            var applyBold = false;
            PdfFontDescriptor? descriptor = font.GetDescriptor();
            if (descriptor != null)
            {
                // Check font flags for bold, or check the font name for "Bold"
                bool isBoldFlag = descriptor.IsBold;
                bool isBoldName = font.BaseFont?.Contains("Bold", StringComparison.OrdinalIgnoreCase) == true;

                // Also use StemV as heuristic - higher values indicate bolder fonts
                // Regular fonts typically have StemV < 100, bold fonts > 150
                // Values 120-150 suggest semi-bold or bold variants without "Bold" in name
                bool isBoldStemV = descriptor.StemV >= 120;

                applyBold = isBoldFlag || isBoldName || isBoldStemV;
            }

            // Get embedded font metrics
            EmbeddedFontMetrics? embeddedMetrics = font.GetEmbeddedMetrics();
            if (embeddedMetrics is not { IsValid: true })
            {
                PdfLogger.Log(LogCategory.Text, $"No embedded metrics for font '{font.BaseFont}'");
                return false;
            }

            // Calculate the horizontal scaling factor early so we can use it for character advances
            // HorizontalScaling is stored as a percentage (100 = 100% = 1.0 scale)
            float tHs = (float)state.HorizontalScaling / 100f;

            // Position for rendering glyphs
            double currentX = 0;

            // Iterate over character codes, not Unicode characters
            // One character code can decode to multiple Unicode chars (e.g., ligatures)
            int loopCount = charCodes?.Count ?? text.Length;

            for (var i = 0; i < loopCount; i++)
            {
                // Get character code - either from original PDF codes or fall back to Unicode
                ushort charCode = charCodes != null && i < charCodes.Count
                    ? (ushort)charCodes[i]
                    : text[i];

                // Get corresponding character for logging (may not match 1:1 due to ligatures)
                char displayChar = i < text.Length ? text[i] : '?';

                ushort glyphId;

                // For CFF fonts without cmap, use glyph name mapping via the PDF encoding
                if (embeddedMetrics.IsCffFont && font.Encoding is not null)
                {
                    string? glyphName = font.Encoding.GetGlyphName(charCode);
                    glyphId = glyphName is not null
                        ? embeddedMetrics.GetGlyphIdByName(glyphName)
                        // Fall back to direct lookup
                        : embeddedMetrics.GetGlyphId(charCode);
                }
                else
                {
                    // For Type0/CID fonts, map CID to GID using CIDToGIDMap
                    if (font is Type0Font { DescendantFont: CidFont cidFont })
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
                        currentX += glyphWidths[i] * tHs;  // Apply horizontal scaling to advance
                    continue;
                }

                // Extract glyph outline
                GlyphOutline? glyphOutline = embeddedMetrics.GetGlyphOutline(glyphId);
                if (glyphOutline == null)
                {
                    if (i < glyphWidths.Count)
                        currentX += glyphWidths[i] * tHs;  // Apply horizontal scaling to advance
                    continue;
                }

                if (glyphOutline.IsEmpty)
                {
                    // Empty glyph (e.g., space), just advance
                    if (i < glyphWidths.Count)
                        currentX += glyphWidths[i] * tHs;  // Apply horizontal scaling to advance
                    continue;
                }

                // Create a cache key: font name + glyph ID + font size (rounded for precision)
                string fontKey = font.BaseFont ?? "unknown";
                var roundedSize = (int)(state.FontSize * 10); // 0.1pt precision
                var cacheKey = $"{fontKey}_{glyphId}_{roundedSize}";

                // Try to get from the cache first
                SKPath glyphPath;
                if (GlyphPathCache.TryGetValue(cacheKey, out SKPath? cachedPath) && cachedPath != null)
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
                        FontParser.Tables.Cff.GlyphOutline? cffOutline = embeddedMetrics.GetCffGlyphOutlineDirect(glyphId);
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

                PdfLogger.Log(LogCategory.Text, $"GlyphTransformMatrix: HScale={tHs:F2}, Rise={tRise:F2} (FontSize={state.FontSize:F2} already in path)");
                PdfLogger.Log(LogCategory.Text, $"  TextMatrix=[{state.TextMatrix.M11:F4}, {state.TextMatrix.M12:F4}, {state.TextMatrix.M21:F4}, {state.TextMatrix.M22:F4}, {state.TextMatrix.M31:F4}, {state.TextMatrix.M32:F4}]");
                PdfLogger.Log(LogCategory.Text, $"  Result=[{glyphMatrix.M11:F4}, {glyphMatrix.M12:F4}, {glyphMatrix.M21:F4}, {glyphMatrix.M22:F4}, {glyphMatrix.M31:F4}, {glyphMatrix.M32:F4}]");

                // Add translation for the current glyph position (in text space)
                // The translation needs to be applied BEFORE the glyph transformation
                // so it gets scaled by the TextMatrix
                var translationMatrix = Matrix3x2.CreateTranslation((float)currentX, 0);
                Matrix3x2 fullGlyphMatrix = translationMatrix * glyphMatrix;

                // Convert to SKMatrix and apply to path
                // Negate ScaleY to flip the glyph upright, since the canvas has a Y-flip applied
                var skGlyphMatrix = new SKMatrix
                {
                    ScaleX = fullGlyphMatrix.M11,
                    SkewY = fullGlyphMatrix.M12,
                    SkewX = fullGlyphMatrix.M21,
                    ScaleY = -fullGlyphMatrix.M22,  // Negate to counter canvas Y-flip
                    TransX = fullGlyphMatrix.M31,
                    TransY = fullGlyphMatrix.M32,
                    Persp0 = 0,
                    Persp1 = 0,
                    Persp2 = 1
                };
                glyphPath.Transform(skGlyphMatrix);

                // Log drawing details
                SKRect bounds = glyphPath.Bounds;
                PdfLogger.Log(LogCategory.Text, $"DRAW: Page={CurrentPageNumber} X={currentX:F2} GlyphId={glyphId} Bounds=[L:{bounds.Left:F2},T:{bounds.Top:F2},R:{bounds.Right:F2},B:{bounds.Bottom:F2}] GlyphMatrix=[{fullGlyphMatrix.M11:F2},{fullGlyphMatrix.M12:F2},{fullGlyphMatrix.M21:F2},{fullGlyphMatrix.M22:F2},{fullGlyphMatrix.M31:F2},{fullGlyphMatrix.M32:F2}]");

                // Render the glyph
                _canvas.DrawPath(glyphPath, paint);

                // Apply synthetic bold by stroking the glyph outline
                if (applyBold)
                {
                    // Calculate CTM scaling factor for stroke width
                    // Note: Since canvas already has CTM transform, this may cause double-scaling
                    // But we apply it for consistency with line width handling
                    Matrix3x2 ctm = state.Ctm;
                    double ctmScale = Math.Sqrt(Math.Abs(ctm.M11 * ctm.M22 - ctm.M12 * ctm.M21));
                    if (ctmScale < 0.0001) ctmScale = 1.0;

                    using var strokePaint = new SKPaint();
                    strokePaint.Color = paint.Color;
                    strokePaint.IsAntialias = true;
                    strokePaint.Style = SKPaintStyle.Stroke;
                    strokePaint.StrokeWidth = (float)(state.FontSize * 0.04 * ctmScale);
                    _canvas.DrawPath(glyphPath, strokePaint);
                }

                // Clean up path
                glyphPath.Dispose();

                // Advance to the next glyph position
                if (i < glyphWidths.Count)
                    currentX += glyphWidths[i] * tHs;  // Apply horizontal scaling to advance
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
        {
            PdfLogger.Log(LogCategory.Graphics, $"Warning: No color components for colorSpace={colorSpace}");
            return SKColors.Black;
        }

        // Log non-standard color spaces
        if (colorSpace != "DeviceGray" && colorSpace != "DeviceRGB" && colorSpace != "DeviceCMYK")
        {
            PdfLogger.Log(LogCategory.Graphics, $"Non-device colorSpace={colorSpace}, components=[{string.Join(", ", colorComponents)}]");
        }

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

            // Create stroke paint
            SKColor strokeColor = ConvertColor(state.StrokeColor, state.StrokeColorSpace);


            // Calculate CTM scaling factor for line width
            // The line width should be scaled by the CTM's linear scaling factor
            // which is the square root of the absolute determinant of the 2x2 portion
            Matrix3x2 ctm = state.Ctm;
            double ctmScale = Math.Sqrt(Math.Abs(ctm.M11 * ctm.M22 - ctm.M12 * ctm.M21));
            if (ctmScale < 0.0001) ctmScale = 1.0; // Avoid division by zero
            double scaledLineWidth = state.LineWidth * ctmScale;

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
                PdfLogger.Log(LogCategory.Graphics, $"Applying dash pattern: [{string.Join(", ", dashIntervals)}] phase={state.DashPhase * ctmScale}");
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

            // Create fill paint
            SKColor fillColor = ConvertColor(state.FillColor, state.FillColorSpace);
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

            // Fill first
            SKColor fillColor = ConvertColor(state.FillColor, state.FillColorSpace);
            using (var fillPaint = new SKPaint())
            {
                fillPaint.Color = fillColor;
                fillPaint.IsAntialias = true;
                fillPaint.Style = SKPaintStyle.Fill;
                _canvas.DrawPath(skPath, fillPaint);
            }

            // Then stroke
            SKColor strokeColor = ConvertColor(state.StrokeColor, state.StrokeColorSpace);

            // Calculate CTM scaling factor for line width
            Matrix3x2 ctm = state.Ctm;
            double ctmScale = Math.Sqrt(Math.Abs(ctm.M11 * ctm.M22 - ctm.M12 * ctm.M21));
            if (ctmScale < 0.0001) ctmScale = 1.0;
            double scaledLineWidth = state.LineWidth * ctmScale;

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
        SKPath skPath = ConvertToSkPath(path);

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

        // Apply a clipping path (persists until RestoreState is called)
        _canvas.ClipPath(skPath, antialias: true);
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
    /// We only need to apply the display matrix (Y-flip) to convert from PDF to screen coordinates.
    /// </summary>
    private void ApplyPathTransformationMatrix(PdfGraphicsState state)
    {
        // Paths are already transformed by CTM during construction (in PDF user space)
        // We need to apply ONLY the display matrix (Y-flip for PDF→screen conversion)
        // NOT the full CTM, otherwise we'd be transforming twice

        var displayMatrix = new SKMatrix
        {
            ScaleX = 1,
            ScaleY = -1,
            TransX = 0,
            TransY = (float)_pageHeight,
            SkewX = 0,
            SkewY = 0,
            Persp0 = 0,
            Persp1 = 0,
            Persp2 = 1
        };

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
            SKBitmap? bitmap = CreateBitmapFromPdfImage(image, fillColor);
            if (bitmap == null)
                return;

            try
            {
                PdfLogger.Log(LogCategory.Images, "DrawImage called");
                PdfLogger.Log(LogCategory.Images, $"  Bitmap size: {bitmap.Width}x{bitmap.Height}");

                var oldMatrix = _canvas.TotalMatrix;
                PdfLogger.Log(LogCategory.Images, $"  Old matrix: [{oldMatrix.ScaleX:F4}, {oldMatrix.SkewY:F4}, {oldMatrix.SkewX:F4}, {oldMatrix.ScaleY:F4}, {oldMatrix.TransX:F4}, {oldMatrix.TransY:F4}]");

                var imageFlipMatrix = new SKMatrix(1, 0, 0, 0, -1, 1, 0, 0, 1);
                PdfLogger.Log(LogCategory.Images, $"  Image flip matrix: [1, 0, 0, 0, -1, 1]");

                var combinedMatrix = oldMatrix.PreConcat(imageFlipMatrix);
                PdfLogger.Log(LogCategory.Images, $"  Combined matrix: [{combinedMatrix.ScaleX:F4}, {combinedMatrix.SkewY:F4}, {combinedMatrix.SkewX:F4}, {combinedMatrix.ScaleY:F4}, {combinedMatrix.TransX:F4}, {combinedMatrix.TransY:F4}]");

                using var paint = new SKPaint
                {
                    FilterQuality = SKFilterQuality.High,
                    IsAntialias = true
                };

                using var skImage = SKImage.FromBitmap(bitmap);
                var sourceRect = new SKRect(0, 0, bitmap.Width, bitmap.Height);
                var destRect = new SKRect(0, 0, 1, 1);

                _canvas.SetMatrix(combinedMatrix);
                _canvas.DrawImage(skImage, sourceRect, destRect, paint);
                _canvas.SetMatrix(oldMatrix);

                PdfLogger.Log(LogCategory.Images, "  Image drawn successfully");
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

            // Check for SMask (alpha channel / transparency)
            byte[]? smaskData = null;
            bool hasSMask = image.HasAlpha;
            if (hasSMask)
            {
                // Extract SMask from the image stream
                PdfStream stream = image.Stream;
                if (stream.Dictionary.TryGetValue(new PdfName("SMask"), out PdfObject? smaskObj))
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
                SKColor color = imageMaskColor.Value;

                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        // Do NOT flip Y here - the image flip matrix in DrawImage will handle it
                        int srcY = y;

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

            switch (colorSpace)
            {
                case "Indexed":
                {
                    // Handle indexed color images
                    byte[]? paletteData = image.GetIndexedPalette(out string? baseColorSpace, out int hival);
                    if (paletteData == null || baseColorSpace == null)
                        return null;

                    // Use Unpremul alpha type for SMask (we set colors without premultiplying)
                    SKAlphaType alphaType = hasSMask ? SKAlphaType.Unpremul : SKAlphaType.Opaque;
                    bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, alphaType);

                    int componentsPerEntry = baseColorSpace switch
                    {
                        "DeviceRGB" => 3,
                        "DeviceGray" => 1,
                        _ => 3
                    };

                    // Convert indexed pixels to RGBA
                    for (var y = 0; y < height; y++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            // Do NOT flip Y here - the image flip matrix in DrawImage will handle it
                            int pixelIndex = y * width + x;
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

                    break;
                }
                case "DeviceRGB" or "CalRGB" when bitsPerComponent == 8:
                {
                    SKAlphaType alphaType = hasSMask ? SKAlphaType.Unpremul : SKAlphaType.Opaque;
                    bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, alphaType);
                    int expectedSize = width * height * 3;
                    if (imageData.Length < expectedSize)
                        return null;

                    for (var y = 0; y < height; y++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            // Do NOT flip Y here - the image flip matrix in DrawImage will handle it
                            int pixelIndex = y * width + x;
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

                    break;
                }
                case "DeviceGray" or "CalGray" when bitsPerComponent == 8:
                {
                    SKAlphaType alphaType = hasSMask ? SKAlphaType.Unpremul : SKAlphaType.Opaque;
                    bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, alphaType);
                    int expectedSize = width * height;
                    if (imageData.Length < expectedSize)
                        return null;

                    for (var y = 0; y < height; y++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            // Do NOT flip Y here - the image flip matrix in DrawImage will handle it
                            int pixelIndex = y * width + x;
                            byte gray = imageData[pixelIndex];

                            // Apply SMask alpha channel if present
                            byte alpha = 255;
                            if (smaskData != null && pixelIndex < smaskData.Length)
                                alpha = smaskData[pixelIndex];

                            bitmap.SetPixel(x, y, new SKColor(gray, gray, gray, alpha));
                        }
                    }

                    break;
                }
                default:
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
        using SKImage? image = _surface.Snapshot();
        using SKData? data = image.Encode(format, quality);
        using FileStream stream = File.OpenWrite(filePath);
        data.SaveTo(stream);
    }

    public void Dispose()
    {
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
}
