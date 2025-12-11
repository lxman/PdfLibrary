using System.Numerics;
using Logging;
using Microsoft.Extensions.Caching.Memory;
using PdfLibrary.Content;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Rendering.SkiaSharp.Conversion;
using SkiaSharp;

namespace PdfLibrary.Rendering.SkiaSharp.Rendering;

/// <summary>
/// Handles text rendering operations for PDF documents.
/// Manages glyph outline rendering, fallback font rendering, and text measurement.
/// </summary>
internal class TextRenderer
{
    private readonly SKCanvas _canvas;
    private readonly GlyphToSKPathConverter _glyphConverter;

    // Static caching for glyph paths to improve performance
    private static readonly MemoryCache GlyphPathCache;
    private static readonly MemoryCacheEntryOptions CacheOptions;
    private static readonly SystemFontResolver FontResolver;

    static TextRenderer()
    {
        var cacheOptions = new MemoryCacheOptions
        {
            SizeLimit = 1000 // Limit to 1000 cached glyph paths
        };
        GlyphPathCache = new MemoryCache(cacheOptions);

        CacheOptions = new MemoryCacheEntryOptions()
            .SetSize(1)
            .SetSlidingExpiration(TimeSpan.FromMinutes(5));

        FontResolver = new SystemFontResolver();
    }

    public TextRenderer(SKCanvas canvas)
    {
        _canvas = canvas;
        _glyphConverter = new GlyphToSKPathConverter();
    }

    /// <summary>
    /// Render text at the current position using the specified graphics state.
    /// Attempts to use embedded font glyph outlines first, falls back to system fonts if needed.
    /// </summary>
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
            SKColor fillColor = ColorConverter.ConvertColor(state.ResolvedFillColor, state.ResolvedFillColorSpace);
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
            RenderWithFallbackFont(text, glyphWidths, state, font, paint);
        }
        finally
        {
            _canvas.Restore();
        }
    }

    /// <summary>
    /// Measure the width of text using fallback font metrics.
    /// </summary>
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
        // Note: SkiaSharp requires positive font size, but PDF can have negative font sizes when using Y-flipped CTM
        using var measureFont = new SKFont(SKTypeface.FromFamilyName(fallbackFontFamily, fontStyle), Math.Abs(effectiveFontSize));
        float width = measureFont.MeasureText(text);

        // Account for horizontal scaling from graphics state
        // HorizontalScaling is stored as percentage (100 = 100% = 1.0 scale)
        float tHs = (float)state.HorizontalScaling / 100f;
        width *= tHs;

        return width;
    }

    /// <summary>
    /// Clear the glyph path cache (called when clearing the render target).
    /// </summary>
    public static void ClearCache()
    {
        GlyphPathCache.Compact(1.0); // Remove all entries
    }

    /// <summary>
    /// Get the number of cached glyph paths.
    /// </summary>
    public static long GetCachedGlyphCount()
    {
        return GlyphPathCache.Count;
    }

    #region Private Helper Methods

    private void RenderWithFallbackFont(string text, List<double> glyphWidths, PdfGraphicsState state, PdfFont? font, SKPaint paint)
    {
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
        // Note: SkiaSharp requires positive font size, but PDF can have negative font sizes when using Y-flipped CTM
        using var fallbackFont = new SKFont(SKTypeface.FromFamilyName(fallbackFontFamily, fontStyle), Math.Abs(effectiveFontSize));

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
            SKColor strokeColor = ColorConverter.ConvertColor(state.ResolvedStrokeColor, state.ResolvedStrokeColorSpace);
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
                // If font size was negative (PDF uses negative font size with Y-flipped CTM):
                // - Flip X (to correct horizontal orientation)
                // - Don't flip Y (negative font size already encodes the Y-flip)
                float xScale = effectiveFontSize < 0 ? -tHs : tHs;
                float yScale = effectiveFontSize < 0 ? 1 : -1;
                _canvas.Scale(xScale, yScale);  // Horizontal scaling and Y-flip (in rotated space)

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
                // Handle horizontal flips:
                // - FontSize < 0 is handled by canvas scaling (line 856-858) which flips glyph visually
                // - Advance direction is determined ONLY by TextMatrix.M11 (text flow direction)
                // - TextMatrix.M11 < 0 means text flows left, so flip advance
                double advanceSign = state.TextMatrix.M11 < 0 ? -1.0 : 1.0;
                currentX += Convert.ToSingle(pdfWidth * advanceSign);
            }
        }
        finally
        {
            strokePaint?.Dispose();
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

                ushort glyphId = ResolveGlyphId(embeddedMetrics, font, charCode, displayChar, out string? resolvedGlyphName);

                if (glyphId == 0)
                {
                    PdfLogger.Log(LogCategory.Text, $"GLYPH-SKIP-ZERO: char='{displayChar}' (charCode={charCode}), glyphId=0, skipping");
                    // Glyph not found, skip this character
                    if (i < glyphWidths.Count)
                    {
                        AdvancePosition(ref currentX, glyphWidths[i], state);
                    }
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

                    // Try to render em dash fallback if applicable
                    if (charCode == 151 && i < glyphWidths.Count && glyphWidths[i] > 0.1f)
                    {
                        RenderEmDashFallback(glyphWidths[i], state, paint, currentX, tHs);
                    }

                    if (i < glyphWidths.Count)
                    {
                        AdvancePosition(ref currentX, glyphWidths[i], state);
                    }
                    continue;
                }

                if (glyphOutline.IsEmpty)
                {
                    PdfLogger.Log(LogCategory.Text, $"GLYPH-SKIP-EMPTY: char='{displayChar}' (charCode={charCode}), glyphId={glyphId}, glyphOutline.IsEmpty=true");
                    // Empty glyph (e.g., space), just advance
                    if (i < glyphWidths.Count)
                    {
                        AdvancePosition(ref currentX, glyphWidths[i], state);
                    }
                    continue;
                }

                // Get or create glyph path
                SKPath glyphPath = GetOrCreateGlyphPath(font, glyphId, state, embeddedMetrics, glyphOutline, resolvedGlyphName);

                // Transform and render the glyph
                RenderGlyph(glyphPath, state, paint, currentX, tHs, displayChar, applyBold);

                // Clean up path
                glyphPath.Dispose();

                // Advance to the next glyph position
                if (i < glyphWidths.Count)
                {
                    AdvancePosition(ref currentX, glyphWidths[i], state);
                }
            }

            return true; // Successfully rendered with glyph outlines
        }
        catch
        {
            // If anything fails, fall back to default rendering
            return false;
        }
    }

    private ushort ResolveGlyphId(EmbeddedFontMetrics embeddedMetrics, PdfFont font, ushort charCode, char displayChar, out string? resolvedGlyphName)
    {
        ushort glyphId;
        resolvedGlyphName = null;

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

        return glyphId;
    }

    private void RenderEmDashFallback(double glyphWidth, PdfGraphicsState state, SKPaint paint, double currentX, float tHs)
    {
        // Draw em dash as a horizontal line/rectangle
        // Em dash is typically at about 40% of the em height, with height about 5-8% of em
        float emDashY = (float)state.FontSize * 0.35f;  // Position at ~35% up from baseline
        float emDashHeight = (float)state.FontSize * 0.06f;  // ~6% of font size
        float emDashWidth = (float)glyphWidth * (float)state.FontSize;  // Full width scaled by font size

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

    private SKPath GetOrCreateGlyphPath(PdfFont font, ushort glyphId, PdfGraphicsState state, EmbeddedFontMetrics embeddedMetrics, GlyphOutline glyphOutline, string? resolvedGlyphName)
    {
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

        return glyphPath;
    }

    private void RenderGlyph(SKPath glyphPath, PdfGraphicsState state, SKPaint paint, double currentX, float tHs, char displayChar, bool applyBold)
    {
        // IMPORTANT: The glyph converter (ConvertToPath/ConvertCffToPath) already scales
        // the glyph by FontSize when converting from font units to device units.
        // Therefore, we should NOT include FontSize in the transformation matrix here,
        // or we'll get double font size scaling!

        var tRise = (float)state.TextRise;

        var textStateMatrix = new Matrix3x2(
            tHs, 0,      // Scale X by HorizontalScale only (NO FontSize - already in path!)
            0, 1,        // No Y scaling (FontSize already in path!)
            0, tRise     // Translate Y by TextRise
        );

        // Multiply by text matrix to get the complete glyph transformation
        Matrix3x2 glyphMatrix = textStateMatrix * state.TextMatrix;

        // Add translation for the current glyph position (in text space)
        var translationMatrix = Matrix3x2.CreateTranslation((float)currentX, 0);
        Matrix3x2 fullGlyphMatrix = translationMatrix * glyphMatrix;

        // Apply rotation-aware Y-flip compensation
        SKMatrix skGlyphMatrix = CreateYFlipCompensatedMatrix(fullGlyphMatrix, displayChar);
        glyphPath.Transform(skGlyphMatrix);

        // Render the glyph based on text rendering mode
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
                devicePath.FillType = SKPathFillType.EvenOdd;
                PdfLogger.Log(LogCategory.Text, $"GLYPH-FILLTYPE: char='{displayChar}', devicePath FillType explicitly set to EvenOdd (testing)");

                SKRect deviceBounds = devicePath.Bounds;
                PdfLogger.Log(LogCategory.Text, $"GLYPH-RENDER: devicePath bounds=({deviceBounds.Left:F2},{deviceBounds.Top:F2},{deviceBounds.Right:F2},{deviceBounds.Bottom:F2})");

                // DEBUG: Log path contour information
                PdfLogger.Log(LogCategory.Text, $"GLYPH-PATH-INFO: char='{displayChar}', ContourCount={devicePath.PointCount}, IsEmpty={devicePath.IsEmpty}");

                // DEBUG: Dump detailed path data for 'a' to see if inner contour is present
                if (displayChar == 'a')
                {
                    LogPathDetails(devicePath);
                }

                // DEBUG: Check if there's a clip region active BEFORE drawing
                SKRect localClip = _canvas.LocalClipBounds;
                SKRect deviceClip = _canvas.DeviceClipBounds;
                PdfLogger.Log(LogCategory.Text, $"CLIP-BEFORE-GLYPH: char='{displayChar}', LocalClip=({localClip.Left:F2},{localClip.Top:F2},{localClip.Right:F2},{localClip.Bottom:F2}), DeviceClip=({deviceClip.Left:F2},{deviceClip.Top:F2},{deviceClip.Right:F2},{deviceClip.Bottom:F2})");

                _canvas.Save();
                _canvas.ResetMatrix();

                // DEBUG: Check clip bounds AFTER ResetMatrix
                SKRect clipAfterReset = _canvas.LocalClipBounds;
                PdfLogger.Log(LogCategory.Text, $"CLIP-AFTER-RESET: char='{displayChar}', LocalClip=({clipAfterReset.Left:F2},{clipAfterReset.Top:F2},{clipAfterReset.Right:F2},{clipAfterReset.Bottom:F2}), devicePath bounds=({deviceBounds.Left:F2},{deviceBounds.Top:F2},{deviceBounds.Right:F2},{deviceBounds.Bottom:F2})");
                _canvas.DrawPath(devicePath, paint);
                _canvas.Restore();
            }

            if (shouldStroke)
            {
                // Stroke the glyph outline
                SKColor strokeColor = ColorConverter.ConvertColor(state.ResolvedStrokeColor, state.ResolvedStrokeColorSpace);
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
    }

    private SKMatrix CreateYFlipCompensatedMatrix(Matrix3x2 fullGlyphMatrix, char displayChar)
    {
        // The canvas has a global Y-flip (y → -y) applied in BeginPage
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

        return new SKMatrix
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
    }

    private void AdvancePosition(ref double currentX, double glyphWidth, PdfGraphicsState state)
    {
        // Handle horizontal flips: Use XOR logic because:
        // - FontSize < 0 scales glyph by negative value, causing horizontal flip
        // - TextMatrix.M11 < 0 flips via transformation matrix
        // - One flip = backwards, two flips = normal
        bool flipX = (state.FontSize < 0) != (state.TextMatrix.M11 < 0);  // XOR
        double advanceSign = flipX ? -1.0 : 1.0;
        currentX += glyphWidth * advanceSign;
    }

    private void LogPathDetails(SKPath devicePath)
    {
        var pathIterator = devicePath.CreateRawIterator();
        SKPoint[] points = new SKPoint[4];
        int commandIndex = 0;
        SKPathVerb verb;
        System.Text.StringBuilder pathDump = new System.Text.StringBuilder();
        pathDump.AppendLine($"PATH-DUMP for 'a': FillType={devicePath.FillType}");
        while ((verb = pathIterator.Next(points)) != SKPathVerb.Done)
        {
            pathDump.Append($"  [{commandIndex}] {verb}");
            if (verb == SKPathVerb.Move)
                pathDump.AppendLine($" to ({points[0].X:F2}, {points[0].Y:F2})");
            else if (verb == SKPathVerb.Line)
                pathDump.AppendLine($" to ({points[1].X:F2}, {points[1].Y:F2})");
            else if (verb == SKPathVerb.Quad)
                pathDump.AppendLine($" ctrl({points[1].X:F2}, {points[1].Y:F2}) to ({points[2].X:F2}, {points[2].Y:F2})");
            else if (verb == SKPathVerb.Conic)
                pathDump.AppendLine($" ctrl({points[1].X:F2}, {points[1].Y:F2}) w={pathIterator.ConicWeight():F2} to ({points[2].X:F2}, {points[2].Y:F2})");
            else if (verb == SKPathVerb.Cubic)
                pathDump.AppendLine($" ctrl1({points[1].X:F2}, {points[1].Y:F2}) ctrl2({points[2].X:F2}, {points[2].Y:F2}) to ({points[3].X:F2}, {points[3].Y:F2})");
            else if (verb == SKPathVerb.Close)
                pathDump.AppendLine();
            commandIndex++;
        }
        PdfLogger.Log(LogCategory.Text, pathDump.ToString());
    }

    private static SKColor ApplyAlpha(SKColor color, double alpha)
    {
        if (alpha >= 1.0)
            return color;

        var alphaByte = (byte)(Math.Clamp(alpha, 0.0, 1.0) * 255);
        return new SKColor(color.Red, color.Green, color.Blue, alphaByte);
    }

    #endregion
}
