using System.Buffers;
using Jp2Codec;
using Logging;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering.Icc;
using PdfLibrary.Structure;

namespace PdfLibrary.Rendering;

/// <summary>
/// Describes how the alpha channel relates to the RGB channels in a <see cref="PdfImageToRgba.RgbaImage"/>.
/// </summary>
public enum AlphaMode
{
    /// <summary>All pixels are fully opaque; the alpha channel should be ignored by the renderer.</summary>
    Opaque,
    /// <summary>Alpha is straight (un-premultiplied); RGB has not been scaled by alpha.</summary>
    Unpremultiplied,
    /// <summary>RGB channels have been premultiplied by alpha (image-mask stencils, indexed images with SMask).</summary>
    Premultiplied
}

/// <summary>
/// Converts a <see cref="PdfImage"/> to a raw RGBA8888 pixel buffer (R,G,B,A order,
/// top-row-first) with no SkiaSharp dependency.
///
/// <para>Unsupported cases that return <see langword="null"/>:</para>
/// <list type="bullet">
///   <item>16 bpc images — not yet implemented.</item>
///   <item>Lab colour space — not yet implemented.</item>
///   <item>Unknown colour-space / bpc combination — falls to default.</item>
/// </list>
/// </summary>
public static class PdfImageToRgba
{
    /// <summary>
    /// Carries the decoded RGBA pixel plane together with its dimensions and alpha mode.
    /// </summary>
    /// <param name="Rgba">RGBA8888 bytes: R, G, B, A order, top-row first.</param>
    /// <param name="Width">Image width in pixels.</param>
    /// <param name="Height">Image height in pixels.</param>
    /// <param name="Alpha">
    ///   <see cref="AlphaMode.Premultiplied"/> for image-mask stencils and indexed images with SMask
    ///   (RGB has been scaled by alpha); <see cref="AlphaMode.Unpremultiplied"/> for straight-alpha
    ///   images (those with an applied SMask); <see cref="AlphaMode.Opaque"/> for fully-opaque images.
    /// </param>
    public readonly record struct RgbaImage(byte[] Rgba, int Width, int Height, AlphaMode Alpha);

    /// <summary>
    /// Decodes <paramref name="image"/> to an RGBA8888 byte array.
    /// Returns <see langword="null"/> for unsupported images (16 bpc, Lab, unknown colour space).
    /// </summary>
    public static RgbaImage? ToRgba(
        PdfImage image,
        PdfDocument? doc,
        (byte R, byte G, byte B, byte A)? imageMaskColor = null,
        bool blackPointCompensation = false,
        string? renderingIntent = null)
    {
        try
        {
            byte[] imageData = image.GetDecodedData();
            int width = image.Width;
            int height = image.Height;
            int bitsPerComponent = image.BitsPerComponent;
            string colorSpace = image.ColorSpace;

            if (colorSpace == "ICCBased")
            {
                int iccComponents = GetIccBasedComponentCount(image, doc);
                colorSpace = iccComponents switch
                {
                    1 => "DeviceGray",
                    3 => "ICCBased",
                    4 => "ICCBased",
                    _ => "ICCBased"
                };
            }

            // Branch 1: JPXDecode
            PdfStream stream = image.Stream;
            if (stream.Dictionary.TryGetValue(new PdfName("Filter"), out PdfObject? filterObj))
            {
                List<string> filters = [];
                if (filterObj is PdfName filterName)
                    filters.Add(filterName.Value);
                else if (filterObj is PdfArray filterArray)
                    filters.AddRange(filterArray.OfType<PdfName>().Select(n => n.Value));

                if (filters.Contains("JPXDecode"))
                {
                    try
                    {
                        byte[] rawJp2Data = stream.Data;
                        DateTime decodeStart = DateTime.Now;
                        byte[] pixelData = Jpeg2000.Decompress(rawJp2Data, out int jp2Width, out int jp2Height, out int components);
                        TimeSpan decodeElapsed = DateTime.Now - decodeStart;
                        PdfLogger.Log(LogCategory.Images, $"[TIMING] JPEG2000 decode took {decodeElapsed.TotalMilliseconds:F0}ms for {jp2Width}x{jp2Height} image with {components} components");

                        DateTime convertStart = DateTime.Now;
                        byte[] rgba = ConvertRawBytesToRgba(pixelData, jp2Width, jp2Height, components);
                        TimeSpan convertElapsed = DateTime.Now - convertStart;
                        PdfLogger.Log(LogCategory.Images, $"[TIMING] Raw bytes→RGBA conversion took {convertElapsed.TotalMilliseconds:F0}ms for {jp2Width}x{jp2Height} image");

                        return new RgbaImage(rgba, jp2Width, jp2Height, AlphaMode.Opaque);
                    }
                    catch (Exception ex)
                    {
                        PdfLogger.Log(LogCategory.Images, $"Manual JP2/JPEG decode failed, falling back to standard processing: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            // SMask preamble (shared by branches 2–10)
            byte[]? smaskData = null;
            byte[]? rentedSmask = null;

            bool hasActualSMask = stream.Dictionary.ContainsKey(new PdfName("SMask"));

            if (hasActualSMask)
            {
                if (stream.Dictionary.TryGetValue(new PdfName("SMask"), out PdfObject? smaskObj))
                {
                    if (smaskObj is PdfIndirectReference smaskRef && doc is not null)
                        smaskObj = doc.ResolveReference(smaskRef);

                    if (smaskObj is PdfStream smaskStream)
                    {
                        var smaskImage = new PdfImage(smaskStream, doc);
                        byte[] rawSmaskData = smaskImage.GetDecodedData();

                        int expectedGrayscaleSize = smaskImage.Width * smaskImage.Height;
                        int expectedRgbSize = expectedGrayscaleSize * 3;

                        if (rawSmaskData.Length == expectedRgbSize)
                        {
                            rentedSmask = ArrayPool<byte>.Shared.Rent(expectedGrayscaleSize);
                            for (var i = 0; i < expectedGrayscaleSize; i++)
                                rentedSmask[i] = rawSmaskData[i * 3];
                            smaskData = rentedSmask;
                        }
                        else
                        {
                            smaskData = rawSmaskData;
                        }
                    }
                }
            }

            try
            {
                double[]? imgDecodeArray = image.DecodeArray;
                string decodeStr = imgDecodeArray is not null ? $"[{string.Join(", ", imgDecodeArray)}]" : "null";
                int debugExpectedRgb = width * height * 3;
                PdfLogger.Log(LogCategory.Images, $"PdfImageToRgba: ColorSpace='{colorSpace}', BitsPerComponent={bitsPerComponent}, Width={width}, Height={height}, DataLength={imageData.Length}, ExpectedRGB={debugExpectedRgb}, Decode={decodeStr}");

                // Branch 2: Image mask (1-bit stencil)
                if (image.IsImageMask && imageMaskColor.HasValue)
                {
                    (byte colorR, byte colorG, byte colorB, byte colorA) = imageMaskColor.Value;

                    double[]? decodeArray = image.DecodeArray;
                    bool invertMask = decodeArray is { Length: >= 2 } && decodeArray[0] > decodeArray[1];

                    int bytesPerRow = (width + 7) / 8;
                    int pixelBufferSize = width * height * 4;
                    byte[] pixelBuffer = ArrayPool<byte>.Shared.Rent(pixelBufferSize);

                    for (var y = 0; y < height; y++)
                    {
                        int rowStart = y * bytesPerRow;
                        int bufferRowStart = y * width * 4;

                        for (var x = 0; x < width; x++)
                        {
                            int byteIndex = rowStart + (x >> 3);
                            int bitOffset = 7 - (x & 7);
                            int bufferOffset = bufferRowStart + (x << 2);

                            if (byteIndex >= imageData.Length)
                            {
                                pixelBuffer[bufferOffset] = 0;
                                pixelBuffer[bufferOffset + 1] = 0;
                                pixelBuffer[bufferOffset + 2] = 0;
                                pixelBuffer[bufferOffset + 3] = 0;
                                continue;
                            }

                            bool bitIsSet = ((imageData[byteIndex] >> bitOffset) & 1) == 1;
                            bool paint = invertMask ? bitIsSet : !bitIsSet;

                            if (paint)
                            {
                                pixelBuffer[bufferOffset] = colorR;
                                pixelBuffer[bufferOffset + 1] = colorG;
                                pixelBuffer[bufferOffset + 2] = colorB;
                                pixelBuffer[bufferOffset + 3] = colorA;
                            }
                            else
                            {
                                pixelBuffer[bufferOffset] = 0;
                                pixelBuffer[bufferOffset + 1] = 0;
                                pixelBuffer[bufferOffset + 2] = 0;
                                pixelBuffer[bufferOffset + 3] = 0;
                            }
                        }
                    }

                    byte[] rgba2 = new byte[pixelBufferSize];
                    Array.Copy(pixelBuffer, rgba2, pixelBufferSize);
                    ArrayPool<byte>.Shared.Return(pixelBuffer);
                    return new RgbaImage(rgba2, width, height, AlphaMode.Premultiplied);
                }

                byte[] result;
                AlphaMode alphaMode;

                switch (colorSpace)
                {
                    case "Indexed":
                    {
                        byte[]? paletteData = image.GetIndexedPalette(out string? baseColorSpace, out int hival);
                        if (paletteData is null || baseColorSpace is null)
                            return null;

                        int componentsPerEntry = baseColorSpace switch
                        {
                            "DeviceRGB" => 3,
                            "DeviceGray" => 1,
                            _ => 3
                        };

                        alphaMode = hasActualSMask ? AlphaMode.Premultiplied : AlphaMode.Opaque;
                        int pixelBufferSize = width * height * 4;
                        byte[] pixelBuffer = ArrayPool<byte>.Shared.Rent(pixelBufferSize);

                        int debugPixelCount = Math.Min(10, width * height);
                        int bytesPerRowIdx = (width * bitsPerComponent + 7) / 8;

                        for (var y = 0; y < height; y++)
                        {
                            for (var x = 0; x < width; x++)
                            {
                                byte paletteIndex;
                                switch (bitsPerComponent)
                                {
                                    case 8:
                                    {
                                        int idx = y * bytesPerRowIdx + x;
                                        paletteIndex = idx < imageData.Length ? imageData[idx] : (byte)0;
                                        break;
                                    }
                                    case 4:
                                    {
                                        int idx = y * bytesPerRowIdx + (x >> 1);
                                        if (idx < imageData.Length)
                                        {
                                            paletteIndex = (x & 1) == 0
                                                ? (byte)((imageData[idx] >> 4) & 0x0F)
                                                : (byte)(imageData[idx] & 0x0F);
                                        }
                                        else paletteIndex = 0;
                                        break;
                                    }
                                    case 2:
                                    {
                                        int idx = y * bytesPerRowIdx + (x >> 2);
                                        int shift = (3 - (x & 3)) * 2;
                                        paletteIndex = idx < imageData.Length
                                            ? (byte)((imageData[idx] >> shift) & 0x03)
                                            : (byte)0;
                                        break;
                                    }
                                    case 1:
                                    {
                                        int idx = y * bytesPerRowIdx + (x >> 3);
                                        int shift = 7 - (x & 7);
                                        paletteIndex = idx < imageData.Length
                                            ? (byte)((imageData[idx] >> shift) & 0x01)
                                            : (byte)0;
                                        break;
                                    }
                                    default:
                                    {
                                        int idx = y * width + x;
                                        paletteIndex = idx < imageData.Length ? imageData[idx] : (byte)0;
                                        break;
                                    }
                                }

                                if (paletteIndex > hival)
                                    paletteIndex = (byte)hival;

                                int pixelIndex = y * width + x;
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

                                        if (pixelIndex < debugPixelCount)
                                        {
                                            PdfLogger.Log(LogCategory.Images, $"INDEXED PIXEL[{pixelIndex}]: index={paletteIndex}, offset={paletteOffset}, RGB=({r}, {g}, {b})");
                                        }

                                        alpha = 255;
                                        if (smaskData is not null && pixelIndex < smaskData.Length)
                                        {
                                            alpha = smaskData[pixelIndex];
                                            if (hasActualSMask && alpha < 255)
                                            {
                                                r = (byte)(r * alpha / 255);
                                                g = (byte)(g * alpha / 255);
                                                b = (byte)(b * alpha / 255);
                                            }
                                        }

                                        break;
                                    }
                                    case 1 when paletteOffset < paletteData.Length:
                                    {
                                        byte gray = paletteData[paletteOffset];

                                        alpha = 255;
                                        if (smaskData is not null && pixelIndex < smaskData.Length)
                                        {
                                            alpha = smaskData[pixelIndex];
                                            if (hasActualSMask && alpha < 255)
                                            {
                                                gray = (byte)(gray * alpha / 255);
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

                                pixelBuffer[bufferOffset] = r;
                                pixelBuffer[bufferOffset + 1] = g;
                                pixelBuffer[bufferOffset + 2] = b;
                                pixelBuffer[bufferOffset + 3] = alpha;
                            }
                        }

                        result = new byte[pixelBufferSize];
                        Array.Copy(pixelBuffer, result, pixelBufferSize);
                        ArrayPool<byte>.Shared.Return(pixelBuffer);
                        return new RgbaImage(result, width, height, alphaMode);
                    }
                    case "DeviceRGB" or "CalRGB" when bitsPerComponent == 8:
                    {
                        alphaMode = smaskData is not null ? AlphaMode.Unpremultiplied : AlphaMode.Opaque;
                        int expectedSize = width * height * 3;
                        if (imageData.Length < expectedSize)
                            return null;

                        if (colorSpace == "CalRGB")
                        {
                            CalRgbConverter? cal = CalRgbConverter.FromCalRgbArray(image.ColorSpaceArray, doc);
                            if (cal is not null)
                                imageData = CalibrateCalRgbBuffer(imageData, expectedSize, cal);
                        }

                        int pixelBufferSize = width * height * 4;
                        byte[] pixelBuffer = ArrayPool<byte>.Shared.Rent(pixelBufferSize);

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

                                byte alpha = 255;
                                if (smaskData is not null && pixelIndex < smaskData.Length)
                                    alpha = smaskData[pixelIndex];

                                pixelBuffer[dstOffset] = r;
                                pixelBuffer[dstOffset + 1] = g;
                                pixelBuffer[dstOffset + 2] = b;
                                pixelBuffer[dstOffset + 3] = alpha;
                            }
                        }

                        int expectedRowBytes = width * 4;
                        result = new byte[pixelBufferSize];
                        if (width * 4 != expectedRowBytes)
                        {
                            // this branch never triggers (width*4 always == expectedRowBytes) but preserves logic
                            for (var row = 0; row < height; row++)
                            {
                                int srcOff = row * expectedRowBytes;
                                Array.Copy(pixelBuffer, srcOff, result, srcOff, expectedRowBytes);
                            }
                        }
                        else
                        {
                            Array.Copy(pixelBuffer, result, pixelBufferSize);
                        }
                        ArrayPool<byte>.Shared.Return(pixelBuffer);
                        return new RgbaImage(result, width, height, alphaMode);
                    }
                    case "DeviceGray" or "CalGray" when bitsPerComponent == 1:
                    {
                        int bytesPerRow = (width + 7) / 8;
                        int pixelBufferSize1Bit = width * height * 4;
                        byte[] pixelBuffer1Bit = ArrayPool<byte>.Shared.Rent(pixelBufferSize1Bit);

                        for (var y = 0; y < height; y++)
                        {
                            int rowStart = y * bytesPerRow;
                            int bufferRowStart = y * width * 4;
                            for (var x = 0; x < width; x++)
                            {
                                int byteIndex = rowStart + (x >> 3);
                                int bitOffset = 7 - (x & 7);
                                int bufferOffset = bufferRowStart + (x << 2);

                                byte gray = 255;
                                if (byteIndex < imageData.Length)
                                {
                                    bool bitIsSet = ((imageData[byteIndex] >> bitOffset) & 1) == 1;
                                    gray = bitIsSet ? (byte)255 : (byte)0;
                                }

                                pixelBuffer1Bit[bufferOffset] = gray;
                                pixelBuffer1Bit[bufferOffset + 1] = gray;
                                pixelBuffer1Bit[bufferOffset + 2] = gray;
                                pixelBuffer1Bit[bufferOffset + 3] = 255;
                            }
                        }

                        result = new byte[pixelBufferSize1Bit];
                        Array.Copy(pixelBuffer1Bit, result, pixelBufferSize1Bit);
                        ArrayPool<byte>.Shared.Return(pixelBuffer1Bit);
                        return new RgbaImage(result, width, height, AlphaMode.Opaque);
                    }
                    case "DeviceGray" or "CalGray" when bitsPerComponent == 8:
                    {
                        alphaMode = smaskData is not null ? AlphaMode.Unpremultiplied : AlphaMode.Opaque;
                        int expectedSize = width * height;
                        int expectedRgbSizeGray = expectedSize * 3;

                        if (imageData.Length == expectedRgbSizeGray)
                        {
                            byte[] grayData = ArrayPool<byte>.Shared.Rent(expectedSize);
                            for (var i = 0; i < expectedSize; i++)
                                grayData[i] = imageData[i * 3];
                            imageData = grayData[..expectedSize];
                            ArrayPool<byte>.Shared.Return(grayData);
                        }

                        if (imageData.Length < expectedSize)
                            return null;

                        int pixelBufferSize = width * height * 4;
                        byte[] pixelBuffer = ArrayPool<byte>.Shared.Rent(pixelBufferSize);

                        for (var y = 0; y < height; y++)
                        {
                            int rowStart = y * width;
                            int bufferRowStart = rowStart * 4;
                            for (var x = 0; x < width; x++)
                            {
                                int pixelIndex = rowStart + x;
                                int bufferOffset = bufferRowStart + (x << 2);
                                byte gray = imageData[pixelIndex];

                                byte alpha = 255;
                                if (smaskData is not null && pixelIndex < smaskData.Length)
                                    alpha = smaskData[pixelIndex];

                                pixelBuffer[bufferOffset] = gray;
                                pixelBuffer[bufferOffset + 1] = gray;
                                pixelBuffer[bufferOffset + 2] = gray;
                                pixelBuffer[bufferOffset + 3] = alpha;
                            }
                        }

                        result = new byte[pixelBufferSize];
                        Array.Copy(pixelBuffer, result, pixelBufferSize);
                        ArrayPool<byte>.Shared.Return(pixelBuffer);
                        return new RgbaImage(result, width, height, alphaMode);
                    }
                    case "ICCBased" when bitsPerComponent == 8:
                    {
                        int numComponents = GetIccBasedComponentCount(image, doc);

                        PdfStream? iccProfile = GetIccProfileStream(image, doc);
                        if (iccProfile is not null && numComponents is 1 or 3 or 4)
                        {
                            int needed = width * height * numComponents;
                            if (imageData.Length >= needed)
                            {
                                byte[] src = imageData.Length == needed ? imageData : imageData[..needed];
                                byte[]? managed = new IccColorConverter(doc)
                                    .TryConvertInterleavedToSrgb(iccProfile, src, numComponents, blackPointCompensation, renderingIntent);
                                if (managed is not null)
                                {
                                    imageData = managed;
                                    numComponents = 3;
                                }
                            }
                        }

                        switch (numComponents)
                        {
                            case 3:
                            {
                                alphaMode = smaskData is not null ? AlphaMode.Unpremultiplied : AlphaMode.Opaque;
                                int pixelBufferSize = width * height * 4;
                                byte[] pixelBuffer = ArrayPool<byte>.Shared.Rent(pixelBufferSize);
                                int availablePixels = Math.Min(imageData.Length / 3, width * height);

                                for (var i = 0; i < availablePixels; i++)
                                {
                                    int srcOffset = i * 3;
                                    int dstOffset = i * 4;
                                    pixelBuffer[dstOffset] = imageData[srcOffset];
                                    pixelBuffer[dstOffset + 1] = imageData[srcOffset + 1];
                                    pixelBuffer[dstOffset + 2] = imageData[srcOffset + 2];
                                    pixelBuffer[dstOffset + 3] = smaskData is not null && i < smaskData.Length ? smaskData[i] : (byte)255;
                                }

                                for (int i = availablePixels; i < width * height; i++)
                                {
                                    int dstOffset = i * 4;
                                    pixelBuffer[dstOffset] = 255;
                                    pixelBuffer[dstOffset + 1] = 255;
                                    pixelBuffer[dstOffset + 2] = 255;
                                    pixelBuffer[dstOffset + 3] = 0;
                                }

                                result = new byte[pixelBufferSize];
                                Array.Copy(pixelBuffer, result, pixelBufferSize);
                                ArrayPool<byte>.Shared.Return(pixelBuffer);
                                return new RgbaImage(result, width, height, alphaMode);
                            }
                            case 1:
                            {
                                alphaMode = smaskData is not null ? AlphaMode.Unpremultiplied : AlphaMode.Opaque;
                                int expectedSize = width * height;
                                if (imageData.Length < expectedSize)
                                    return null;

                                int pixelBufferSize = width * height * 4;
                                byte[] pixelBuffer = ArrayPool<byte>.Shared.Rent(pixelBufferSize);
                                int pixelCount = width * height;
                                for (var i = 0; i < pixelCount; i++)
                                {
                                    byte gray = imageData[i];
                                    int dstOffset = i * 4;
                                    pixelBuffer[dstOffset] = gray;
                                    pixelBuffer[dstOffset + 1] = gray;
                                    pixelBuffer[dstOffset + 2] = gray;
                                    pixelBuffer[dstOffset + 3] = smaskData is not null && i < smaskData.Length ? smaskData[i] : (byte)255;
                                }

                                result = new byte[pixelBufferSize];
                                Array.Copy(pixelBuffer, result, pixelBufferSize);
                                ArrayPool<byte>.Shared.Return(pixelBuffer);
                                return new RgbaImage(result, width, height, alphaMode);
                            }
                            case 4:
                            {
                                alphaMode = smaskData is not null ? AlphaMode.Unpremultiplied : AlphaMode.Opaque;
                                int expectedSize = width * height * 4;
                                if (imageData.Length < expectedSize)
                                    return null;

                                int pixelBufferSize = width * height * 4;
                                byte[] pixelBuffer = ArrayPool<byte>.Shared.Rent(pixelBufferSize);
                                int pixelCount = width * height;
                                for (var i = 0; i < pixelCount; i++)
                                {
                                    int srcOffset = i * 4;
                                    int dstOffset = i * 4;
                                    int c = imageData[srcOffset];
                                    int m = imageData[srcOffset + 1];
                                    int yy = imageData[srcOffset + 2];
                                    int k = imageData[srcOffset + 3];
                                    (byte rr, byte gg, byte bb) = CmykToRgb((byte)c, (byte)m, (byte)yy, (byte)k);
                                    pixelBuffer[dstOffset] = rr;
                                    pixelBuffer[dstOffset + 1] = gg;
                                    pixelBuffer[dstOffset + 2] = bb;
                                    pixelBuffer[dstOffset + 3] = smaskData is not null && i < smaskData.Length ? smaskData[i] : (byte)255;
                                }

                                result = new byte[pixelBufferSize];
                                Array.Copy(pixelBuffer, result, pixelBufferSize);
                                ArrayPool<byte>.Shared.Return(pixelBuffer);
                                return new RgbaImage(result, width, height, alphaMode);
                            }
                            default:
                                return null;
                        }
                    }
                    case "DeviceCMYK" when bitsPerComponent == 8:
                    {
                        alphaMode = smaskData is not null ? AlphaMode.Unpremultiplied : AlphaMode.Opaque;
                        int expectedCmykSize = width * height * 4;
                        int expectedRgbSizeCmyk = width * height * 3;
                        int pixelCount = width * height;
                        int pixelBufferSize = width * height * 4;
                        byte[] pixelBuffer = ArrayPool<byte>.Shared.Rent(pixelBufferSize);

                        // Honour a non-identity /Decode array — how genuinely-inverted Adobe CMYK
                        // JPEGs request inversion (DCTDecode no longer guesses from the Adobe marker).
                        // The default [0 1 0 1 0 1 0 1] is a no-op.
                        double[]? dec = image.DecodeArray;
                        bool applyDecode = dec is { Length: >= 8 } &&
                            (dec[0] != 0 || dec[1] != 1 || dec[2] != 0 || dec[3] != 1 ||
                             dec[4] != 0 || dec[5] != 1 || dec[6] != 0 || dec[7] != 1);

                        if (imageData.Length >= expectedCmykSize)
                        {
                            for (var i = 0; i < pixelCount; i++)
                            {
                                int srcOffset = i * 4;
                                int dstOffset = i * 4;
                                int c = imageData[srcOffset];
                                int m = imageData[srcOffset + 1];
                                int y = imageData[srcOffset + 2];
                                int k = imageData[srcOffset + 3];
                                if (applyDecode)
                                {
                                    c = DecodeSample(c, dec![0], dec[1]);
                                    m = DecodeSample(m, dec[2], dec[3]);
                                    y = DecodeSample(y, dec[4], dec[5]);
                                    k = DecodeSample(k, dec[6], dec[7]);
                                }
                                (byte rr, byte gg, byte bb) = CmykToRgb((byte)c, (byte)m, (byte)y, (byte)k);
                                pixelBuffer[dstOffset] = rr;
                                pixelBuffer[dstOffset + 1] = gg;
                                pixelBuffer[dstOffset + 2] = bb;
                                pixelBuffer[dstOffset + 3] = smaskData is not null && i < smaskData.Length ? smaskData[i] : (byte)255;
                            }
                        }
                        else if (imageData.Length >= expectedRgbSizeCmyk)
                        {
                            PdfLogger.Log(LogCategory.Images, () => $"DeviceCMYK->RGB first pixels: [{imageData[0]},{imageData[1]},{imageData[2]}] [{imageData[3]},{imageData[4]},{imageData[5]}] [{imageData[6]},{imageData[7]},{imageData[8]}]");

                            for (var i = 0; i < pixelCount; i++)
                            {
                                int srcOffset = i * 3;
                                int dstOffset = i * 4;
                                pixelBuffer[dstOffset] = imageData[srcOffset];
                                pixelBuffer[dstOffset + 1] = imageData[srcOffset + 1];
                                pixelBuffer[dstOffset + 2] = imageData[srcOffset + 2];
                                pixelBuffer[dstOffset + 3] = smaskData is not null && i < smaskData.Length ? smaskData[i] : (byte)255;
                            }
                        }
                        else
                        {
                            ArrayPool<byte>.Shared.Return(pixelBuffer);
                            return null;
                        }

                        result = new byte[pixelBufferSize];
                        Array.Copy(pixelBuffer, result, pixelBufferSize);
                        ArrayPool<byte>.Shared.Return(pixelBuffer);
                        return new RgbaImage(result, width, height, alphaMode);
                    }
                    case "Separation" or "DeviceN" when bitsPerComponent == 8:
                    {
                        // Each pixel is N colorant samples; resolve them through the tint transform →
                        // alternate → RGB (the same mapping fills and indexed palettes use).
                        PdfArray? csArray = image.ColorSpaceArray;
                        if (csArray is null) return null;
                        Func<double[], (byte R, byte G, byte B)>? tintToRgb =
                            ColorSpaceResolver.BuildTintToRgb(csArray, doc, out int nComps);
                        if (tintToRgb is null || nComps < 1) return null;

                        int pixelCount = width * height;
                        if (imageData.Length < pixelCount * nComps) return null;

                        alphaMode = smaskData is not null ? AlphaMode.Unpremultiplied : AlphaMode.Opaque;
                        int pixelBufferSize = pixelCount * 4;
                        byte[] pixelBuffer = ArrayPool<byte>.Shared.Rent(pixelBufferSize);

                        // Memoize repeated colorant tuples — duotones reuse a handful of colours, so a
                        // per-pixel tint evaluation would be wasteful.
                        var cache = new Dictionary<uint, (byte R, byte G, byte B)>();
                        bool packable = nComps <= 4;
                        var colorants = new double[nComps];

                        // Honour a non-identity /Decode array (e.g. [1 0] to invert a spot ramp): map
                        // each colorant sample through its interval before the tint transform, exactly
                        // as the DeviceCMYK branch does. The default [0 1 …] per component is a no-op.
                        double[]? decode = image.DecodeArray;
                        bool applyDecode = decode is not null && decode.Length >= nComps * 2;

                        for (var p = 0; p < pixelCount; p++)
                        {
                            int src = p * nComps;
                            (byte R, byte G, byte B) rgb;
                            if (packable)
                            {
                                uint key = 0;
                                for (var c = 0; c < nComps; c++) key = (key << 8) | imageData[src + c];
                                if (!cache.TryGetValue(key, out rgb))
                                {
                                    for (var c = 0; c < nComps; c++)
                                        colorants[c] = (applyDecode ? DecodeSample(imageData[src + c], decode![2 * c], decode[2 * c + 1]) : imageData[src + c]) / 255.0;
                                    rgb = tintToRgb(colorants);
                                    cache[key] = rgb;
                                }
                            }
                            else
                            {
                                for (var c = 0; c < nComps; c++)
                                    colorants[c] = (applyDecode ? DecodeSample(imageData[src + c], decode![2 * c], decode[2 * c + 1]) : imageData[src + c]) / 255.0;
                                rgb = tintToRgb(colorants);
                            }

                            int off = p * 4;
                            pixelBuffer[off] = rgb.R;
                            pixelBuffer[off + 1] = rgb.G;
                            pixelBuffer[off + 2] = rgb.B;
                            pixelBuffer[off + 3] = smaskData is not null && p < smaskData.Length ? smaskData[p] : (byte)255;
                        }

                        result = new byte[pixelBufferSize];
                        Array.Copy(pixelBuffer, result, pixelBufferSize);
                        ArrayPool<byte>.Shared.Return(pixelBuffer);
                        return new RgbaImage(result, width, height, alphaMode);
                    }
                    default:
                        return null;
                }
            }
            finally
            {
                if (rentedSmask is not null)
                    ArrayPool<byte>.Shared.Return(rentedSmask);
            }
        }
        catch (Exception ex)
        {
            PdfLogger.Log(LogCategory.Images, $"PdfImageToRgba.ToRgba exception: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Consolidated CMYK→RGB conversion: r=(255-c)*(255-k)/255, same formula used in all CMYK branches.</summary>
    /// <summary>Maps an 8-bit sample through a /Decode interval [dMin, dMax] back to an 8-bit value.</summary>
    private static int DecodeSample(int sample, double dMin, double dMax) =>
        (int)Math.Clamp(Math.Round((dMin + sample / 255.0 * (dMax - dMin)) * 255.0), 0, 255);

    private static (byte R, byte G, byte B) CmykToRgb(byte c, byte m, byte y, byte k) =>
        ((byte)((255 - c) * (255 - k) / 255),
         (byte)((255 - m) * (255 - k) / 255),
         (byte)((255 - y) * (255 - k) / 255));

    /// <summary>
    /// Converts raw interleaved pixel bytes from JPEG 2000 decode to RGBA8888.
    /// 1-comp → gray expanded; 3-comp → RGB+255alpha; 4-comp → CMYK→RGB+255alpha.
    /// </summary>
    private static byte[] ConvertRawBytesToRgba(byte[] pixelData, int width, int height, int components)
    {
        int pixelCount = width * height;
        byte[] rgba = new byte[pixelCount * 4];
        switch (components)
        {
            case 1:
                for (var i = 0; i < pixelCount; i++)
                {
                    byte gray = pixelData[i];
                    rgba[i * 4]     = gray;
                    rgba[i * 4 + 1] = gray;
                    rgba[i * 4 + 2] = gray;
                    rgba[i * 4 + 3] = 255;
                }
                break;
            case 3:
                for (var i = 0; i < pixelCount; i++)
                {
                    rgba[i * 4]     = pixelData[i * 3];
                    rgba[i * 4 + 1] = pixelData[i * 3 + 1];
                    rgba[i * 4 + 2] = pixelData[i * 3 + 2];
                    rgba[i * 4 + 3] = 255;
                }
                break;
            case 4:
                for (var i = 0; i < pixelCount; i++)
                {
                    int s = i * 4;
                    (byte r, byte g, byte b) = CmykToRgb(pixelData[s], pixelData[s+1], pixelData[s+2], pixelData[s+3]);
                    rgba[s]     = r;
                    rgba[s + 1] = g;
                    rgba[s + 2] = b;
                    rgba[s + 3] = 255;
                }
                break;
            default:
                throw new NotSupportedException($"Number of components {components} not supported");
        }
        return rgba;
    }

    private static byte[] CalibrateCalRgbBuffer(byte[] rgb, int count, CalRgbConverter converter)
    {
        var dst = new byte[rgb.Length];
        if (rgb.Length > count)
            Array.Copy(rgb, count, dst, count, rgb.Length - count);
        for (var i = 0; i + 2 < count; i += 3)
        {
            double[] s = converter.ToSrgb(rgb[i] / 255.0, rgb[i + 1] / 255.0, rgb[i + 2] / 255.0);
            dst[i]     = ToSrgbByte(s[0]);
            dst[i + 1] = ToSrgbByte(s[1]);
            dst[i + 2] = ToSrgbByte(s[2]);
        }
        return dst;
    }

    private static byte ToSrgbByte(double v) => v <= 0.0 ? (byte)0 : v >= 1.0 ? (byte)255 : (byte)Math.Round(v * 255.0);

    private static PdfStream? GetIccProfileStream(PdfImage image, PdfDocument? doc)
    {
        PdfArray? cs = image.ColorSpaceArray;
        if (cs is not { Count: >= 2 } || cs[0] is not PdfName { Value: "ICCBased" })
            return null;
        PdfObject? streamObj = cs[1];
        if (streamObj is PdfIndirectReference r && doc is not null)
            streamObj = doc.ResolveReference(r);
        return streamObj as PdfStream;
    }

    private static int GetIccBasedComponentCount(PdfImage image, PdfDocument? doc)
    {
        try
        {
            if (!image.Stream.Dictionary.TryGetValue(new PdfName("ColorSpace"), out PdfObject? csObj))
                return 3;

            if (csObj is PdfIndirectReference reference && doc is not null)
                csObj = doc.ResolveReference(reference);

            if (csObj is not PdfArray { Count: >= 2 } csArray)
                return 3;

            PdfObject? streamObj = csArray[1];
            if (streamObj is PdfIndirectReference streamRef && doc is not null)
                streamObj = doc.ResolveReference(streamRef);

            if (streamObj is not PdfStream iccStream)
                return 3;

            if (iccStream.Dictionary.TryGetValue(new PdfName("N"), out PdfObject? nObj) && nObj is PdfInteger nInt)
                return nInt.Value;

            return 3;
        }
        catch
        {
            return 3;
        }
    }
}
