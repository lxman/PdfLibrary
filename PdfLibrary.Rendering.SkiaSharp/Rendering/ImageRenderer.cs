using System.Buffers;
using System.Runtime.InteropServices;
using Logging;
using PdfLibrary.Content;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Rendering.SkiaSharp.Conversion;
using PdfLibrary.Structure;
using SkiaSharp;

namespace PdfLibrary.Rendering.SkiaSharp.Rendering;

/// <summary>
/// Handles image rendering operations for PDF documents.
/// Manages bitmap creation from various PDF image formats, color spaces, and compression methods.
/// </summary>
internal class ImageRenderer
{
    private readonly SKCanvas _canvas;
    private readonly PdfDocument? _document;

    public ImageRenderer(SKCanvas canvas, PdfDocument? document)
    {
        _canvas = canvas;
        _document = document;
    }

    /// <summary>
    /// Draws a PDF image to the canvas using the current graphics state.
    /// </summary>
    public void DrawImage(PdfImage image, PdfGraphicsState state)
    {
        try
        {
            // Create SKBitmap from PDF image data
            // For image masks, pass the fill color to use as the stencil color (with alpha)
            SKColor? fillColor = null;
            if (image.IsImageMask)
            {
                fillColor = ColorConverter.ConvertColor(state.ResolvedFillColor, state.ResolvedFillColorSpace);
                fillColor = ApplyAlpha(fillColor.Value, state.FillAlpha);
            }
            // An image's own /Intent overrides the current graphics-state rendering intent.
            SKBitmap? bitmap = CreateBitmapFromPdfImage(image, fillColor, state.UseBlackPointCompensation, image.Intent ?? state.RenderingIntent);
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
                }

                // Debug: Log canvas clip bounds
                SKRect clipBounds = _canvas.LocalClipBounds;
                SKRectI deviceClipBounds = _canvas.DeviceClipBounds;
                PdfLogger.Log(LogCategory.Images, $"DrawImage: ClipBounds Local=({clipBounds.Left:F2},{clipBounds.Top:F2},{clipBounds.Right:F2},{clipBounds.Bottom:F2}), Device=({deviceClipBounds.Left},{deviceClipBounds.Top},{deviceClipBounds.Right},{deviceClipBounds.Bottom})");

                // Wrap the bitmap pixels without copying — bitmap stays alive for the duration of Draw.
                using SKImage? skImage = SKImage.FromPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes);
                if (skImage == null)
                {
                    PdfLogger.Log(LogCategory.Images, "DrawImage: SKImage.FromPixels returned null!");
                    _canvas.SetMatrix(oldMatrix);
                    return;
                }

                // Draw image into unit square with a tiny expansion to cover sub-pixel gaps
                // Some PDFs have tiled images with fractional pixel gaps due to coordinate rounding.
                // A small expansion (0.001 in unit space) prevents visible seams without affecting quality.
                var sourceRect = new SKRect(0, 0, bitmap.Width, bitmap.Height);
                const float epsilon = 0.002f;  // Small expansion to cover sub-pixel gaps
                var destRect = new SKRect(-epsilon, -epsilon, 1 + epsilon, 1 + epsilon);

                // Resampler choice depends on scale direction and the image's /Interpolate flag —
                // see ChooseImageSampling.
                float deviceW = MathF.Sqrt(combinedMatrix.ScaleX * combinedMatrix.ScaleX + combinedMatrix.SkewY * combinedMatrix.SkewY);
                float deviceH = MathF.Sqrt(combinedMatrix.SkewX * combinedMatrix.SkewX + combinedMatrix.ScaleY * combinedMatrix.ScaleY);
                SKSamplingOptions sampling = ChooseImageSampling(bitmap.Width, bitmap.Height, deviceW, deviceH, image.Interpolate);

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

    /// <summary>
    /// Creates a SkiaSharp bitmap from PDF image data by delegating to the SkiaSharp-free
    /// <see cref="PdfImageToRgba.ToRgba"/> core helper, then pinning the result into an
    /// <see cref="SKBitmap"/> via Marshal.Copy.
    /// </summary>
    private SKBitmap? CreateBitmapFromPdfImage(PdfImage image, SKColor? imageMaskColor = null,
        bool blackPointCompensation = false, string? renderingIntent = null)
    {
        (byte, byte, byte, byte)? maskColor = imageMaskColor is { } c ? (c.Red, c.Green, c.Blue, c.Alpha) : null;
        PdfImageToRgba.RgbaImage? r = PdfImageToRgba.ToRgba(image, _document, maskColor, blackPointCompensation, renderingIntent);
        if (r is not { } img) return null;

        var info = new SKImageInfo(img.Width, img.Height, SKColorType.Rgba8888,
            img.IsPremultiplied ? SKAlphaType.Premul : SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);
        Marshal.Copy(img.Rgba, 0, bitmap.GetPixels(), img.Rgba.Length);
        bitmap.NotifyPixelsChanged();
        return bitmap;
    }

    /// <summary>
    /// Chooses the SkiaSharp resampler for drawing an image of <paramref name="bitmapWidth"/>×
    /// <paramref name="bitmapHeight"/> source pixels into a target of <paramref name="deviceWidth"/>×
    /// <paramref name="deviceHeight"/> device pixels.
    ///
    /// Minifying (source larger than target) always uses mipmapped linear: a fixed-kernel cubic
    /// undersamples a high-resolution scan and washes out thin strokes, while mipmaps pre-average.
    /// Magnifying or 1:1 honours the PDF /Interpolate flag: the default (false) means do not smooth
    /// on upscaling, so a low-resolution image stays crisp (nearest-neighbour) rather than blurring
    /// its pixels into one another — matching PDFium/Acrobat. Cubic is used only when /Interpolate
    /// is true.
    /// </summary>
    internal static SKSamplingOptions ChooseImageSampling(
        int bitmapWidth, int bitmapHeight, float deviceWidth, float deviceHeight, bool interpolate)
    {
        bool minifying = bitmapWidth > deviceWidth * 1.1f || bitmapHeight > deviceHeight * 1.1f;
        if (minifying)
            return new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);

        return interpolate
            ? new SKSamplingOptions(new SKCubicResampler(0, 0.5f))
            : new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
    }

    /// <summary>
    /// Converts raw interleaved pixel bytes to a SkiaSharp <see cref="SKBitmap"/>.
    /// Kept as an internal helper so that <c>Jp2CmykRenderTests</c> can exercise the
    /// CMYK→RGB conversion in isolation without requiring a full PDF document.
    /// </summary>
    internal static SKBitmap ConvertRawBytesToSkBitmap(byte[] pixelData, int width, int height, int components)
    {
        switch (components)
        {
            case 1:
            {
                var bitmap = new SKBitmap(width, height, SKColorType.Gray8, SKAlphaType.Opaque);
                IntPtr ptr = bitmap.GetPixels();
                if (ptr != IntPtr.Zero)
                {
                    Marshal.Copy(pixelData, 0, ptr, pixelData.Length);
                    bitmap.NotifyPixelsChanged();
                }
                return bitmap;
            }
            case 3:
            {
                var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                int bufSize = width * height * 4;
                byte[] buf = ArrayPool<byte>.Shared.Rent(bufSize);
                for (var i = 0; i < width * height; i++)
                {
                    buf[i * 4]     = pixelData[i * 3];
                    buf[i * 4 + 1] = pixelData[i * 3 + 1];
                    buf[i * 4 + 2] = pixelData[i * 3 + 2];
                    buf[i * 4 + 3] = 255;
                }
                IntPtr ptr = bitmap.GetPixels();
                if (ptr != IntPtr.Zero)
                {
                    Marshal.Copy(buf, 0, ptr, bufSize);
                    bitmap.NotifyPixelsChanged();
                }
                ArrayPool<byte>.Shared.Return(buf);
                return bitmap;
            }
            case 4:
            {
                // A 4-component JP2 in a PDF is CMYK, not RGBA — convert CMYK→RGB.
                var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
                int bufSize = width * height * 4;
                byte[] buf = ArrayPool<byte>.Shared.Rent(bufSize);
                int pixelCount = width * height;
                for (var i = 0; i < pixelCount; i++)
                {
                    int s = i * 4;
                    int c = pixelData[s], m = pixelData[s + 1], y = pixelData[s + 2], k = pixelData[s + 3];
                    buf[s]     = (byte)((255 - c) * (255 - k) / 255);
                    buf[s + 1] = (byte)((255 - m) * (255 - k) / 255);
                    buf[s + 2] = (byte)((255 - y) * (255 - k) / 255);
                    buf[s + 3] = 255;
                }
                IntPtr ptr = bitmap.GetPixels();
                if (ptr != IntPtr.Zero)
                {
                    Marshal.Copy(buf, 0, ptr, bufSize);
                    bitmap.NotifyPixelsChanged();
                }
                ArrayPool<byte>.Shared.Return(buf);
                return bitmap;
            }
            default:
                throw new NotSupportedException($"Number of components {components} not supported for conversion to SKBitmap");
        }
    }

    /// <summary>
    /// Applies alpha transparency to a color.
    /// </summary>
    private static SKColor ApplyAlpha(SKColor color, double alpha)
    {
        var newAlpha = (byte)(color.Alpha * alpha);
        return new SKColor(color.Red, color.Green, color.Blue, newAlpha);
    }
}
