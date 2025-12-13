using System.Runtime.InteropServices;
using Compressors.Jpeg2000;
using Logging;
using PdfLibrary.Content;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
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

    /// <summary>
    /// Creates a SkiaSharp bitmap from PDF image data, handling various color spaces and formats.
    /// </summary>
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
                        DateTime decodeStart = DateTime.Now;
                        byte[] pixelData = Jpeg2000.Decompress(rawJp2Data, out int jp2Width, out int jp2Height, out int components);
                        TimeSpan decodeElapsed = DateTime.Now - decodeStart;
                        PdfLogger.Log(LogCategory.Images, $"[TIMING] JPEG2000 decode took {decodeElapsed.TotalMilliseconds:F0}ms for {jp2Width}x{jp2Height} image with {components} components");

                        // TIMING: Measure raw bytes→SKBitmap conversion
                        DateTime convertStart = DateTime.Now;
                        SKBitmap jp2Bitmap = ConvertRawBytesToSkBitmap(pixelData, jp2Width, jp2Height, components);
                        TimeSpan convertElapsed = DateTime.Now - convertStart;
                        PdfLogger.Log(LogCategory.Images, $"[TIMING] Raw bytes→SKBitmap conversion took {convertElapsed.TotalMilliseconds:F0}ms for {jp2Width}x{jp2Height} image");

                        return jp2Bitmap;
                    }
                    catch (Exception)
                    {
                        // Fall through to standard processing if manual decode fails
                    }
                }
            }

            // Check for SMask (alpha channel / transparency)
            byte[]? smaskData = null;

            // Check if image has SMask (soft mask - actual alpha channel)
            bool hasActualSMask = stream.Dictionary.ContainsKey(new PdfName("SMask"));
            // Check if the image has Mask (color key masking - different from SMask)
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
            SKBitmap? bitmap;

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
                if (bitmapPixels == IntPtr.Zero) return bitmap;
                Marshal.Copy(pixelBuffer, 0, bitmapPixels, pixelBuffer.Length);
                bitmap.NotifyPixelsChanged();


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
                            switch (componentsPerEntry)
                            {
                                case 3 when paletteOffset + 2 < paletteData.Length:
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

                                    break;
                                }
                                case 1 when paletteOffset < paletteData.Length:
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
                                    break;
                                }
                                default:
                                    r = g = b = 0;
                                    alpha = 255;
                                    break;
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

                            // Handle truncated data more gracefully - render what we can instead of failing completely

                            // Use direct pixel buffer for performance
                            var pixelBuffer = new byte[width * height * 4];
                            int availablePixels = Math.Min(imageData.Length / 3, width * height);

                            for (var i = 0; i < availablePixels; i++)
                            {
                                int srcOffset = i * 3;
                                int dstOffset = i * 4;
                                pixelBuffer[dstOffset] = imageData[srcOffset];
                                pixelBuffer[dstOffset + 1] = imageData[srcOffset + 1];
                                pixelBuffer[dstOffset + 2] = imageData[srcOffset + 2];
                                pixelBuffer[dstOffset + 3] = (smaskData is not null && i < smaskData.Length) ? smaskData[i] : (byte)255;
                            }

                            // Fill remaining pixels with transparent or white if data is incomplete
                            for (var i = availablePixels; i < width * height; i++)
                            {
                                int dstOffset = i * 4;
                                pixelBuffer[dstOffset] = 255;     // R
                                pixelBuffer[dstOffset + 1] = 255; // G
                                pixelBuffer[dstOffset + 2] = 255; // B
                                pixelBuffer[dstOffset + 3] = 0;   // A - transparent
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
            PdfLogger.Log(LogCategory.Images, $"CreateBitmapFromPdfImage exception: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets the number of components for an ICCBased colorspace image.
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

    /// <summary>
    /// Converts raw interleaved pixel bytes to SkiaSharp SKBitmap.
    /// </summary>
    /// <param name="pixelData">Raw interleaved pixel data (RGB or RGBA or grayscale)</param>
    /// <param name="width">Image width</param>
    /// <param name="height">Image height</param>
    /// <param name="components">Number of components (1=Gray, 3=RGB, 4=RGBA)</param>
    /// <returns>SKBitmap with decoded image data</returns>
    private static SKBitmap ConvertRawBytesToSkBitmap(byte[] pixelData, int width, int height, int components)
    {
        switch (components)
        {
            case 1: // Grayscale
            {
                var bitmap = new SKBitmap(width, height, SKColorType.Gray8, SKAlphaType.Opaque);
                IntPtr bitmapPixels = bitmap.GetPixels();
                if (bitmapPixels != IntPtr.Zero)
                {
                    Marshal.Copy(pixelData, 0, bitmapPixels, pixelData.Length);
                    bitmap.NotifyPixelsChanged();
                }
                return bitmap;
            }
            case 3: // RGB - convert to RGBA8888 for SkiaSharp
            {
                var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                var rgbaBuffer = new byte[width * height * 4];

                for (var i = 0; i < width * height; i++)
                {
                    rgbaBuffer[i * 4 + 0] = pixelData[i * 3 + 0]; // R
                    rgbaBuffer[i * 4 + 1] = pixelData[i * 3 + 1]; // G
                    rgbaBuffer[i * 4 + 2] = pixelData[i * 3 + 2]; // B
                    rgbaBuffer[i * 4 + 3] = 255;                   // A (opaque)
                }

                IntPtr bitmapPixels = bitmap.GetPixels();
                if (bitmapPixels != IntPtr.Zero)
                {
                    Marshal.Copy(rgbaBuffer, 0, bitmapPixels, rgbaBuffer.Length);
                    bitmap.NotifyPixelsChanged();
                }
                return bitmap;
            }
            case 4: // RGBA
            {
                var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                IntPtr bitmapPixels = bitmap.GetPixels();
                if (bitmapPixels != IntPtr.Zero)
                {
                    Marshal.Copy(pixelData, 0, bitmapPixels, pixelData.Length);
                    bitmap.NotifyPixelsChanged();
                }
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
