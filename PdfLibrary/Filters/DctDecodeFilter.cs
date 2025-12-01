using JpegLibrary;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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
            // Check if this is a CMYK JPEG
            var (isCmyk, isAdobeYcck, colorTransform) = AnalyzeJpegColorSpace(data);

            if (isCmyk)
            {
                // For CMYK/YCCK JPEGs, use JpegLibrary which gives us raw component data
                return DecodeCmykJpegWithJpegLibrary(data, isAdobeYcck, colorTransform);
            }

            // For RGB/Grayscale JPEGs, use ImageSharp (faster for simple cases)
            using var image = Image.Load(data);
            using var normalRgbImage = image.CloneAs<Rgb24>();
            var normalRgbPixels = new byte[normalRgbImage.Width * normalRgbImage.Height * 3];
            normalRgbImage.CopyPixelDataTo(normalRgbPixels);

            return normalRgbPixels;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to decode JPEG data: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Decodes a CMYK/YCCK JPEG using JpegLibrary for proper color handling.
    /// Returns CMYK data (4 components per pixel) - the caller handles CMYK→RGB conversion.
    /// </summary>
    private static byte[] DecodeCmykJpegWithJpegLibrary(byte[] data, bool isAdobeYcck, int colorTransform)
    {
        var decoder = new JpegDecoder();
        decoder.SetInput(data);
        decoder.Identify();

        var width = decoder.Width;
        var height = decoder.Height;
        var numberOfComponents = decoder.NumberOfComponents;

        // Allocate buffer for raw component data (CMYK or YCCK)
        var bufferSize = width * height * numberOfComponents;
        var componentData = new byte[bufferSize];

        // Decode to raw component data without color conversion
        decoder.SetOutputWriter(new JpegBufferOutputWriter8Bit(width, height, numberOfComponents, componentData));
        decoder.Decode();

        // Output buffer for CMYK data (4 components per pixel)
        var cmykPixels = new byte[width * height * 4];
        var pixelCount = width * height;

        if (isAdobeYcck && colorTransform == 2)
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
                var srcIdx = i * 4;
                var dstIdx = i * 4;

                // Raw YCCK components from JPEG
                float y = componentData[srcIdx];
                float cb = componentData[srcIdx + 1];
                float cr = componentData[srcIdx + 2];
                var kStored = componentData[srcIdx + 3];

                // YCbCr → RGB (ITU-R BT.601)
                // These are the INVERTED CMY values
                var rPrime = y + 1.402f * (cr - 128);
                var gPrime = y - 0.344136f * (cb - 128) - 0.714136f * (cr - 128);
                var bPrime = y + 1.772f * (cb - 128);

                // Invert R',G',B' to get actual C,M,Y
                // K is stored as-is (no inversion)
                cmykPixels[dstIdx] = ClampToByte(255 - rPrime);     // C = 255 - R'
                cmykPixels[dstIdx + 1] = ClampToByte(255 - gPrime); // M = 255 - G'
                cmykPixels[dstIdx + 2] = ClampToByte(255 - bPrime); // Y = 255 - B'
                cmykPixels[dstIdx + 3] = kStored;                    // K = K_stored (as-is)
            }
        }
        else
        {
            // Pure CMYK (colorTransform = 0 or no Adobe marker)
            // Adobe CMYK often has inverted values
            var isInverted = colorTransform == 0;

            for (var i = 0; i < pixelCount; i++)
            {
                var srcIdx = i * 4;
                var dstIdx = i * 4;

                var c = componentData[srcIdx];
                var m = componentData[srcIdx + 1];
                var y = componentData[srcIdx + 2];
                var k = componentData[srcIdx + 3];

                if (isInverted)
                {
                    // Inverted CMYK: need to uninvert
                    c = (byte)(255 - c);
                    m = (byte)(255 - m);
                    y = (byte)(255 - y);
                    k = (byte)(255 - k);
                }

                cmykPixels[dstIdx] = c;
                cmykPixels[dstIdx + 1] = m;
                cmykPixels[dstIdx + 2] = y;
                cmykPixels[dstIdx + 3] = k;
            }
        }

        return cmykPixels;
    }

    private static byte ClampToByte(float value)
    {
        return (byte)Math.Max(0, Math.Min(255, Math.Round(value)));
    }

    private static byte ClampToByte(int value)
    {
        return (byte)Math.Max(0, Math.Min(255, value));
    }

    /// <summary>
    /// Analyzes JPEG markers to determine color space and Adobe inversion
    /// </summary>
    /// <returns>Tuple of (isCmyk, isAdobeYcck, colorTransform)</returns>
    private static (bool isCmyk, bool isAdobeYcck, int colorTransform) AnalyzeJpegColorSpace(byte[] data)
    {
        if (data.Length < 20) return (false, false, -1);

        var hasAdobeMarker = false;
        byte adobeColorTransform = 0;
        var numComponents = 0;

        var pos = 2; // Skip SOI marker (FF D8)
        while (pos < data.Length - 4)
        {
            if (data[pos] != 0xFF) break;

            var marker = data[pos + 1];
            if (marker == 0xD9) break; // EOI
            if (marker == 0xDA) break; // SOS - start of scan, stop searching

            // Skip markers without length
            if (marker == 0x00 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                pos += 2;
                continue;
            }

            // Get segment length (big-endian)
            if (pos + 3 >= data.Length) break;
            var length = (data[pos + 2] << 8) | data[pos + 3];
            if (length < 2) break;

            // Check for Adobe APP14 marker (FF EE)
            if (marker == 0xEE && length >= 14 && pos + 16 <= data.Length)
            {
                // Check for "Adobe" signature at pos+4
                if (data[pos + 4] == 'A' && data[pos + 5] == 'd' && data[pos + 6] == 'o' &&
                    data[pos + 7] == 'b' && data[pos + 8] == 'e')
                {
                    hasAdobeMarker = true;
                    // Color transform byte is at offset 11 from segment data start (pos+4)
                    adobeColorTransform = data[pos + 15];
                }
            }

            // Check for SOF markers to get component count
            if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
            {
                // SOF marker - number of components is at offset 7 from marker
                if (pos + 9 < data.Length)
                {
                    numComponents = data[pos + 9];
                }
            }

            pos += 2 + length;
        }

        // Determine if CMYK
        var isCmyk = numComponents == 4;

        // Determine if Adobe YCCK (colorTransform == 2)
        var isAdobeYcck = isCmyk && hasAdobeMarker && adobeColorTransform == 2;

        return (isCmyk, isAdobeYcck, hasAdobeMarker ? adobeColorTransform : -1);
    }
}
