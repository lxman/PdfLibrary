namespace PdfLibrary.Filters;

/// <summary>
/// DCTDecode filter - JPEG compression using Discrete Cosine Transform (ISO 32000-1:2008 section 7.4.8)
/// </summary>
internal class DctDecodeFilter : IStreamFilter
{
    public string Name => "DCTDecode";

    public byte[] Encode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        // For encoding, we'd need to know the image dimensions and format
        // This is typically not used in PDF creation workflows
        throw new NotSupportedException("DCTDecode encoding is not supported. Use pre-compressed JPEG data.");
    }

    public byte[] Decode(byte[] data)
    {
        return Decode(data, null);
    }

    public byte[] Decode(byte[] data, Dictionary<string, object>? parameters)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            // Decode JPEG with metadata (get raw component data, not RGB-converted)
            JpegDecodeResult result = JpegLibraryAdapter.DecodeWithInfo(data, convertToRgb: false);

            int width = result.Width;
            int height = result.Height;
            int componentCount = result.ComponentCount;
            byte[] componentData = result.Data;  // Already interleaved component data

            return componentCount switch
            {
                // Handle different color spaces
                4 => DecodeCmykJpeg(componentData, width, height, result.HasAdobeMarker, result.AdobeColorTransform),
                3 => DecodeRgbJpeg(componentData, width, height),
                1 => componentData,
                _ => throw new NotSupportedException($"Unsupported JPEG component count: {componentCount}")
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to decode JPEG data: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Decodes CMYK/YCCK component data to standard CMYK format.
    /// Returns CMYK data (4 components per pixel) - the caller handles CMYK→RGB conversion.
    /// </summary>
    private static byte[] DecodeCmykJpeg(byte[] componentData, int width, int height, bool hasAdobeMarker, byte adobeColorTransform)
    {
        // Output buffer for CMYK data (4 components per pixel)
        var cmykPixels = new byte[width * height * 4];
        int pixelCount = width * height;

        bool isAdobeYcck = hasAdobeMarker && adobeColorTransform == 2;
        bool isInvertedCmyk = hasAdobeMarker && adobeColorTransform == 0;

        if (isAdobeYcck)
        {
            // Adobe YCCK decoding:
            // In YCCK, Y/Cb/Cr encode the INVERTED C/M/Y values.
            // K is stored as-is (NOT inverted).
            //
            // After YCbCr→RGB conversion, we get (255-C, 255-M, 255-Y):
            //   C = 255 - R' (invert to get actual C)
            //   M = 255 - G' (invert to get actual M)
            //   Y = 255 - B' (invert to get actual Y)
            //   K = K_stored (K is NOT inverted)

            for (var i = 0; i < pixelCount; i++)
            {
                int srcIdx = i * 4;
                int dstIdx = i * 4;

                // Raw YCCK components from JPEG
                float y = componentData[srcIdx];
                float cb = componentData[srcIdx + 1];
                float cr = componentData[srcIdx + 2];
                byte kStored = componentData[srcIdx + 3];

                // YCbCr → RGB (ITU-R BT.601)
                // These are the INVERTED CMY values
                float rPrime = y + 1.402f * (cr - 128);
                float gPrime = y - 0.344136f * (cb - 128) - 0.714136f * (cr - 128);
                float bPrime = y + 1.772f * (cb - 128);

                // Invert R',G',B' to get actual C,M,Y
                // K is stored as-is (no inversion)
                cmykPixels[dstIdx] = ClampToByte(255 - rPrime);     // C = 255 - R'
                cmykPixels[dstIdx + 1] = ClampToByte(255 - gPrime); // M = 255 - G'
                cmykPixels[dstIdx + 2] = ClampToByte(255 - bPrime); // Y = 255 - B'
                cmykPixels[dstIdx + 3] = kStored;                    // K = K_stored (as-is)
            }
        }
        else if (isInvertedCmyk)
        {
            // Inverted CMYK (Adobe colorTransform = 0)
            for (var i = 0; i < pixelCount; i++)
            {
                int srcIdx = i * 4;
                int dstIdx = i * 4;

                // Uninvert CMYK components
                cmykPixels[dstIdx] = (byte)(255 - componentData[srcIdx]);
                cmykPixels[dstIdx + 1] = (byte)(255 - componentData[srcIdx + 1]);
                cmykPixels[dstIdx + 2] = (byte)(255 - componentData[srcIdx + 2]);
                cmykPixels[dstIdx + 3] = (byte)(255 - componentData[srcIdx + 3]);
            }
        }
        else
        {
            // Standard CMYK - copy as-is
            Array.Copy(componentData, cmykPixels, cmykPixels.Length);
        }

        return cmykPixels;
    }

    /// <summary>
    /// Converts YCbCr component data to RGB.
    /// Returns RGB data (3 components per pixel).
    /// </summary>
    private static byte[] DecodeRgbJpeg(byte[] componentData, int width, int height)
    {
        // Convert YCbCr to RGB
        var rgbData = new byte[width * height * 3];
        int pixelCount = width * height;

        for (var i = 0; i < pixelCount; i++)
        {
            int offset = i * 3;
            byte y  = componentData[offset];
            byte cb = componentData[offset + 1];
            byte cr = componentData[offset + 2];

            // YCbCr to RGB conversion (ITU-R BT.601)
            var r = (int)(y + 1.402f * (cr - 128));
            var g = (int)(y - 0.34414f * (cb - 128) - 0.71414f * (cr - 128));
            var b = (int)(y + 1.772f * (cb - 128));

            rgbData[offset]     = (byte)Math.Clamp(r, 0, 255);
            rgbData[offset + 1] = (byte)Math.Clamp(g, 0, 255);
            rgbData[offset + 2] = (byte)Math.Clamp(b, 0, 255);
        }

        return rgbData;
    }

    private static byte ClampToByte(float value)
    {
        return (byte)Math.Max(0, Math.Min(255, Math.Round(value)));
    }
}
