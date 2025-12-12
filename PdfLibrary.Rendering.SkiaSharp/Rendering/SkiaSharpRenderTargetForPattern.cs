using System.Numerics;
using System.Runtime.InteropServices;
using Logging;
using PdfLibrary.Content;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Rendering.SkiaSharp.Conversion;
using SkiaSharp;

namespace PdfLibrary.Rendering.SkiaSharp.Rendering;

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
            using SKPath skPath = PathConverter.ConvertToSkPath(path);
            using var paint = new SKPaint();
            paint.Color = ColorConverter.ConvertColor(state.ResolvedStrokeColor, state.ResolvedStrokeColorSpace);
            paint.IsAntialias = true;
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = Math.Max(0.5f, (float)state.LineWidth);
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
            using SKPath skPath = PathConverter.ConvertToSkPath(path);
            // Set fill type based on PDF operator (evenOdd parameter)
            // Previously we forced EvenOdd when Y-flip was detected, but this causes overlapping
            // paths to create holes. The Y-flip is just a coordinate transform and doesn't
            // require changing the fill rule.
            skPath.FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
            using var paint = new SKPaint();
            paint.Color = ColorConverter.ConvertColor(state.ResolvedFillColor, state.ResolvedFillColorSpace);
            paint.IsAntialias = true;
            paint.Style = SKPaintStyle.Fill;
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
        using SKPath skPath = PathConverter.ConvertToSkPath(path);
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
                Color = ColorConverter.ConvertColor(state.ResolvedFillColor, state.ResolvedFillColorSpace),
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

            if (imageData.Length <= 0) return;
            using SKBitmap? bitmap = CreateBitmapFromImageData(image, imageData);
            PdfLogger.Log(LogCategory.Images, $"PATTERN DrawImage: bitmap is {(bitmap is null ? "NULL" : $"{bitmap.Width}x{bitmap.Height}")}");

            if (bitmap is null) return;
            // Log some pixel values to verify bitmap content
            SKColor pixel0 = bitmap.GetPixel(0, 0);
            SKColor pixelMid = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
            PdfLogger.Log(LogCategory.Images, $"PATTERN DrawImage: bitmap pixel(0,0)=({pixel0.Red},{pixel0.Green},{pixel0.Blue},{pixel0.Alpha}), pixel(mid)=({pixelMid.Red},{pixelMid.Green},{pixelMid.Blue},{pixelMid.Alpha})");

            using var paint = new SKPaint();
            paint.IsAntialias = true;
            paint.FilterQuality = SKFilterQuality.High;
            _canvas.DrawBitmap(bitmap, new SKRect(0, 0, 1, 1), paint);
            PdfLogger.Log(LogCategory.Images, "PATTERN DrawImage: Drew bitmap to (0,0,1,1)");
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

        // Use the rough character width estimate (average character is ~0.5em)
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
                        switch (componentsPerEntry)
                        {
                            case 3 when paletteOffset + 2 < paletteData.Length:
                                r = paletteData[paletteOffset];
                                g = paletteData[paletteOffset + 1];
                                b = paletteData[paletteOffset + 2];
                                break;
                            case 1 when paletteOffset < paletteData.Length:
                            {
                                byte gray = paletteData[paletteOffset];
                                r = g = b = gray;
                                break;
                            }
                            default:
                                r = g = b = 0;
                                break;
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