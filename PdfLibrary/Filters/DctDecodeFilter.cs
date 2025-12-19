using ImageLibrary.Jpeg;

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
            // Decode JPEG to get raw component data (not RGB-converted)
            var decoder = new JpegDecoder(data);
            RawJpegData rawData = decoder.DecodeRaw();

            int width = rawData.Width;
            int height = rawData.Height;
            int componentCount = rawData.ComponentCount;
            byte[] componentData = rawData.ComponentData;  // Interleaved component data

            // For Adobe YCCK (transform=2), treat as YCbCr and return RGB (ignore K)
            if (componentCount == 4 && rawData.HasAdobeMarker && rawData.AdobeColorTransform == 2)
            {
                return DecodeYcckAsRgb(componentData, width, height);
            }

            return componentCount switch
            {
                // Handle different color spaces
                4 => DecodeCmykJpeg(componentData, width, height, rawData.HasAdobeMarker, rawData.AdobeColorTransform),
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
            // K is ALSO stored inverted (255 - K).
            //
            // After YCbCr→RGB conversion, we get (255-C, 255-M, 255-Y):
            //   C = 255 - R' (invert to get actual C)
            //   M = 255 - G' (invert to get actual M)
            //   Y = 255 - B' (invert to get actual Y)
            //   K = 255 - K_stored (K is also inverted)

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
                // Also invert K (stored inverted in Adobe YCCK)
                cmykPixels[dstIdx] = ClampToByte(255 - rPrime);     // C = 255 - R'
                cmykPixels[dstIdx + 1] = ClampToByte(255 - gPrime); // M = 255 - G'
                cmykPixels[dstIdx + 2] = ClampToByte(255 - bPrime); // Y = 255 - B'
                cmykPixels[dstIdx + 3] = (byte)(255 - kStored);     // K = 255 - K_stored (inverted)
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
            // Using double precision and Math.Round() to match ImageLibrary's ColorConverter
            double yVal = y;
            double cbVal = cb - 128.0;
            double crVal = cr - 128.0;

            double r = yVal + 1.402 * crVal;
            double g = yVal - 0.344136 * cbVal - 0.714136 * crVal;
            double b = yVal + 1.772 * cbVal;

            rgbData[offset]     = (byte)Math.Clamp((int)Math.Round(r), 0, 255);
            rgbData[offset + 1] = (byte)Math.Clamp((int)Math.Round(g), 0, 255);
            rgbData[offset + 2] = (byte)Math.Clamp((int)Math.Round(b), 0, 255);
        }

        return rgbData;
    }

    /// <summary>
    /// Decodes Adobe YCCK (transform=2) to RGB.
    /// Adobe YCCK: YCbCr encodes the CMY components, K is the black channel.
    /// Convert YCbCr→RGB to get CMY, then apply K to darken the result.
    /// Returns RGB data (3 components per pixel).
    /// </summary>
    private static byte[] DecodeYcckAsRgb(byte[] componentData, int width, int height)
    {
        var rgbData = new byte[width * height * 3];
        int pixelCount = width * height;

        for (var i = 0; i < pixelCount; i++)
        {
            int srcOffset = i * 4;  // YCCK has 4 components
            int dstOffset = i * 3;  // RGB has 3 components

            byte y  = componentData[srcOffset];
            byte cb = componentData[srcOffset + 1];
            byte cr = componentData[srcOffset + 2];
            byte k  = componentData[srcOffset + 3];

            // YCbCr to RGB conversion (ITU-R BT.601) - gives us the CMY component
            double yVal = y;
            double cbVal = cb - 128.0;
            double crVal = cr - 128.0;

            double r = yVal + 1.402 * crVal;
            double g = yVal - 0.344136 * cbVal - 0.714136 * crVal;
            double b = yVal + 1.772 * cbVal;

            // Apply K channel to darken the RGB result
            // K=0 means no darkening, K=255 means full black
            double kFactor = (255.0 - k) / 255.0;
            r *= kFactor;
            g *= kFactor;
            b *= kFactor;

            rgbData[dstOffset]     = (byte)Math.Clamp((int)Math.Round(r), 0, 255);
            rgbData[dstOffset + 1] = (byte)Math.Clamp((int)Math.Round(g), 0, 255);
            rgbData[dstOffset + 2] = (byte)Math.Clamp((int)Math.Round(b), 0, 255);
        }

        return rgbData;
    }

    private static byte ClampToByte(float value)
    {
        return (byte)Math.Max(0, Math.Min(255, Math.Round(value)));
    }
}
