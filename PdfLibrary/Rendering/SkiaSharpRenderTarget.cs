using System.Numerics;
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

    public int CurrentPageNumber { get; private set; }

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
            Console.WriteLine($"[FALLBACK] Using Arial for '{text.Substring(0, Math.Min(20, text.Length))}' with {glyphWidths.Count} widths");
            using var fallbackFont = new SKFont(SKTypeface.FromFamilyName("Arial"), (float)state.FontSize);

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
            // Get embedded font metrics
            var embeddedMetrics = font.GetEmbeddedMetrics();
            if (embeddedMetrics == null)
            {
                Console.WriteLine($"[GLYPH FAIL] No embedded metrics for '{text.Substring(0, Math.Min(20, text.Length))}' - Font: {font.BaseFont}, Type: {font.FontType}");
                return false;
            }
            if (!embeddedMetrics.IsValid)
            {
                Console.WriteLine($"[GLYPH FAIL] Invalid embedded metrics for '{text.Substring(0, Math.Min(20, text.Length))}' - Font: {font.BaseFont}");
                return false;
            }

            // Position for rendering glyphs
            double currentX = 0;

            Console.WriteLine($"[GLYPH] Rendering '{text}' with {glyphWidths.Count} widths, {charCodes?.Count ?? 0} charCodes");

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                // Get glyph ID for this character
                // Use original PDF character code if available, otherwise fall back to Unicode
                ushort charCode = charCodes != null && i < charCodes.Count
                    ? (ushort)charCodes[i]
                    : (ushort)c;
                ushort glyphId = embeddedMetrics.GetGlyphId(charCode);

                double width = i < glyphWidths.Count ? glyphWidths[i] : 0;
                Console.WriteLine($"[GLYPH]   [{i}] '{c}' charCode={charCode:X4} glyphId={glyphId} width={width:F4} x={currentX:F2}");

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

                // Convert glyph outline to SKPath
                var glyphPath = _glyphConverter.ConvertToPath(
                    glyphOutline,
                    (float)state.FontSize,
                    embeddedMetrics.UnitsPerEm
                );

                // Translate path to current position
                var matrix = SKMatrix.CreateTranslation((float)currentX, 0);
                glyphPath.Transform(matrix);

                // Render the glyph
                _canvas.DrawPath(glyphPath, paint);

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
            return SKColors.Black;

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
            using var paint = new SKPaint
            {
                Color = strokeColor,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)state.LineWidth,
                StrokeCap = ConvertLineCap(state.LineCap),
                StrokeJoin = ConvertLineJoin(state.LineJoin),
                StrokeMiter = (float)state.MiterLimit
            };

            // Apply dash pattern if present
            if (state.DashPattern != null && state.DashPattern.Length > 0)
            {
                var dashIntervals = state.DashPattern.Select(d => (float)d).ToArray();
                paint.PathEffect = SKPathEffect.CreateDash(dashIntervals, (float)state.DashPhase);
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
            using (var strokePaint = new SKPaint
            {
                Color = strokeColor,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)state.LineWidth,
                StrokeCap = ConvertLineCap(state.LineCap),
                StrokeJoin = ConvertLineJoin(state.LineJoin),
                StrokeMiter = (float)state.MiterLimit
            })
            {
                // Apply dash pattern if present
                if (state.DashPattern != null && state.DashPattern.Length > 0)
                {
                    var dashIntervals = state.DashPattern.Select(d => (float)d).ToArray();
                    strokePaint.PathEffect = SKPathEffect.CreateDash(dashIntervals, (float)state.DashPhase);
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
        var pdfMatrix = state.Ctm;
        var displayMatrix = new Matrix3x2(
            1, 0,
            0, -1,
            0, (float)_pageHeight
        );
        var deviceMatrix = pdfMatrix * displayMatrix;

        var skMatrix = new SKMatrix
        {
            ScaleX = deviceMatrix.M11,
            SkewY = deviceMatrix.M12,
            SkewX = deviceMatrix.M21,
            ScaleY = deviceMatrix.M22,
            TransX = deviceMatrix.M31,
            TransY = deviceMatrix.M32,
            Persp0 = 0,
            Persp1 = 0,
            Persp2 = 1
        };

        skPath.Transform(skMatrix);

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
    /// (Different from text transformation - no glyph flip needed)
    /// </summary>
    private void ApplyPathTransformationMatrix(PdfGraphicsState state)
    {
        var pdfMatrix = state.Ctm;

        // Create page-to-device display matrix
        var displayMatrix = new Matrix3x2(
            1, 0,
            0, -1,
            0, (float)_pageHeight
        );

        // Concatenate transformations
        var deviceMatrix = pdfMatrix * displayMatrix;

        // Convert to SKMatrix
        var skMatrix = new SKMatrix
        {
            ScaleX = deviceMatrix.M11,
            SkewY = deviceMatrix.M12,
            SkewX = deviceMatrix.M21,
            ScaleY = deviceMatrix.M22,
            TransX = deviceMatrix.M31,
            TransY = deviceMatrix.M32,
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
            var filters = image.Filters;
            string filterList = filters.Count > 0 ? string.Join(", ", filters) : "None";
            Console.WriteLine($"[DrawImage] Size: {image.Width}x{image.Height}, ColorSpace: {image.ColorSpace}, BPC: {image.BitsPerComponent}, Filters: {filterList}");

            // Create SKBitmap from PDF image data
            var bitmap = CreateBitmapFromPdfImage(image);
            if (bitmap == null)
            {
                Console.WriteLine($"[DrawImage] Failed to create bitmap");
                return;
            }

            Console.WriteLine($"[DrawImage] Bitmap created: {bitmap.Width}x{bitmap.Height}, ColorType: {bitmap.ColorType}");

            _canvas.Save();

            try
            {
                // Apply full transformation matrix following PDFium's approach
                // Images are drawn in a 1x1 unit square at origin, transformed by CTM
                var pdfMatrix = state.Ctm;
                Console.WriteLine($"[DrawImage] CTM: [{pdfMatrix.M11:F2}, {pdfMatrix.M12:F2}, {pdfMatrix.M21:F2}, {pdfMatrix.M22:F2}, {pdfMatrix.M31:F2}, {pdfMatrix.M32:F2}]");

                // Create page-to-device display matrix for images (same as text)
                // For rotation=0: [1, 0, 0, -1, 0, pageHeight]
                // This maps PDF coordinates (x, y) to device coordinates (x, pageHeight - y)
                // Pixels are manually flipped in CreateBitmapFromPdfImage, so the double flip results in correct orientation
                var displayMatrix = new Matrix3x2(
                    1, 0,                       // a, b
                    0, -1,                      // c, d (negative d flips Y-axis)
                    0, (float)_pageHeight       // e, f (translate Y by page height)
                );

                // Concatenate: pdfMatrix * displayMatrix to get device space transformation
                var deviceMatrix = pdfMatrix * displayMatrix;

                Console.WriteLine($"[DrawImage] Device matrix: [{deviceMatrix.M11:F2}, {deviceMatrix.M12:F2}, {deviceMatrix.M21:F2}, {deviceMatrix.M22:F2}, {deviceMatrix.M31:F2}, {deviceMatrix.M32:F2}]");

                // Convert to SKMatrix and apply to canvas
                var skMatrix = new SKMatrix
                {
                    ScaleX = deviceMatrix.M11,
                    SkewY = deviceMatrix.M12,
                    SkewX = deviceMatrix.M21,
                    ScaleY = deviceMatrix.M22,
                    TransX = deviceMatrix.M31,
                    TransY = deviceMatrix.M32,
                    Persp0 = 0,
                    Persp1 = 0,
                    Persp2 = 1
                };

                _canvas.Concat(in skMatrix);

                // Draw image in canonical 1x1 unit square at origin
                // The transformation matrix will position/scale/rotate it correctly
                var unitRect = new SKRect(0, 0, 1, 1);

                // Use high-quality sampling for image scaling
                using var paint = new SKPaint
                {
                    IsAntialias = true
                };

                // Convert bitmap to image for better sampling options support
                using var skImage = SKImage.FromBitmap(bitmap);

                // Use bilinear filtering for downscaling
                var samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                _canvas.DrawImage(skImage, unitRect, samplingOptions, paint);
                Console.WriteLine($"[DrawImage] Bitmap drawn successfully");
            }
            finally
            {
                _canvas.Restore();
                bitmap.Dispose();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DrawImage] Error: {ex.Message}");
            Console.WriteLine($"[DrawImage] Stack: {ex.StackTrace}");
        }
    }

    private SKBitmap? CreateBitmapFromPdfImage(PdfImage image)
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
                Console.WriteLine($"[CreateBitmap] Image has SMask, extracting alpha channel...");
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
                        Console.WriteLine($"[CreateBitmap] SMask extracted: {smaskData.Length} bytes");
                    }
                    else
                    {
                        Console.WriteLine($"[CreateBitmap] WARNING: SMask could not be resolved (type: {smaskObj?.GetType().Name ?? "null"})");
                    }
                }
            }

            // Determine SkiaSharp color type based on PDF color space
            SKBitmap? bitmap = null;

            if (colorSpace == "Indexed")
            {
                // Handle indexed color images
                var paletteData = image.GetIndexedPalette(out string? baseColorSpace, out int hival);
                if (paletteData == null || baseColorSpace == null)
                {
                    Console.WriteLine($"[CreateBitmap] Failed to get indexed palette");
                    return null;
                }

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
            else if (colorSpace == "DeviceRGB" && bitsPerComponent == 8)
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
            else if (colorSpace == "DeviceGray" && bitsPerComponent == 8)
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
                Console.WriteLine($"[DrawImage] Unsupported: {colorSpace}/{bitsPerComponent}bpc");
                return null;
            }

            return bitmap;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CreateBitmap] Error: {ex.Message}");
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
}
