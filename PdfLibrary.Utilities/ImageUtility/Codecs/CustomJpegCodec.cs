using Compressors.Jpeg;

namespace ImageUtility.Codecs;

/// <summary>
/// Custom JPEG codec using the Compressors.Jpeg library.
/// This is the preferred codec for JPEG operations when available.
/// </summary>
public class CustomJpegCodec : IImageCodec
{
    public string Name => "Custom JPEG (Compressors.Jpeg)";
    public string[] Extensions => new[] { ".jpg", ".jpeg" };
    public bool CanDecode => true;
    public bool CanEncode => true;

    public bool CanHandle(ReadOnlySpan<byte> header)
    {
        // JPEG magic bytes: FF D8 FF
        return header.Length >= 3
            && header[0] == 0xFF
            && header[1] == 0xD8
            && header[2] == 0xFF;
    }

    public ImageData Decode(byte[] data)
    {
        // Decode using Compressors.Jpeg
        var result = Jpeg.DecodeWithInfo(data, convertToRgb: true);

        // Determine pixel format based on component count
        PixelFormat pixelFormat;
        if (result.IsGrayscale)
        {
            pixelFormat = PixelFormat.Gray8;
        }
        else if (result.ComponentCount == 3)
        {
            pixelFormat = PixelFormat.Rgb24;
        }
        else if (result.ComponentCount == 4)
        {
            pixelFormat = PixelFormat.Cmyk32;
        }
        else
        {
            throw new NotSupportedException($"Unsupported JPEG component count: {result.ComponentCount}");
        }

        return new ImageData
        {
            Data = result.Data,
            Width = result.Width,
            Height = result.Height,
            PixelFormat = pixelFormat,
            DpiX = 96.0, // JPEG doesn't always include DPI, default to 96
            DpiY = 96.0,
            Metadata = new Dictionary<string, object>
            {
                { "IsGrayscale", result.IsGrayscale },
                { "ComponentCount", result.ComponentCount },
                { "HasAdobeMarker", result.HasAdobeMarker },
                { "AdobeColorTransform", result.AdobeColorTransform }
            }
        };
    }

    public byte[] Encode(ImageData imageData, CodecOptions? options = null)
    {
        // Extract quality from options (default: 95)
        int quality = 95;
        if (options?.Options.TryGetValue("Quality", out object? qualityObj) == true)
        {
            quality = Convert.ToInt32(qualityObj);
            quality = Math.Clamp(quality, 1, 100);
        }

        // Extract subsampling from options (default: 4:2:0)
        var subsampling = JpegSubsampling.Subsampling420;
        if (options?.Options.TryGetValue("Subsampling", out object? subsamplingObj) == true)
        {
            if (subsamplingObj is JpegSubsampling jpegSubsampling)
            {
                subsampling = jpegSubsampling;
            }
            else if (subsamplingObj is string subsamplingStr)
            {
                // Parse string like "4:2:0" or "444"
                subsampling = subsamplingStr switch
                {
                    "4:4:4" or "444" => JpegSubsampling.Subsampling444,
                    "4:2:2" or "422" => JpegSubsampling.Subsampling422,
                    "4:2:0" or "420" => JpegSubsampling.Subsampling420,
                    _ => JpegSubsampling.Subsampling420
                };
            }
        }

        // Encode based on pixel format
        byte[] encodedData;

        if (imageData.PixelFormat == PixelFormat.Gray8)
        {
            // Encode grayscale
            encodedData = Jpeg.EncodeGrayscale(
                imageData.Data,
                imageData.Width,
                imageData.Height,
                quality);
        }
        else if (imageData.PixelFormat == PixelFormat.Rgb24)
        {
            // Encode RGB
            encodedData = Jpeg.Encode(
                imageData.Data,
                imageData.Width,
                imageData.Height,
                quality,
                subsampling);
        }
        else if (imageData.PixelFormat == PixelFormat.Rgba32)
        {
            // Convert RGBA32 to RGB24 first
            byte[] rgb24 = ConvertRgba32ToRgb24(imageData.Data, imageData.Width, imageData.Height);
            encodedData = Jpeg.Encode(
                rgb24,
                imageData.Width,
                imageData.Height,
                quality,
                subsampling);
        }
        else
        {
            throw new NotSupportedException($"Pixel format {imageData.PixelFormat} not supported for JPEG encoding");
        }

        return encodedData;
    }

    private static byte[] ConvertRgba32ToRgb24(byte[] rgba32, int width, int height)
    {
        byte[] rgb24 = new byte[width * height * 3];
        int srcIndex = 0;
        int dstIndex = 0;

        for (int i = 0; i < width * height; i++)
        {
            rgb24[dstIndex++] = rgba32[srcIndex++]; // R
            rgb24[dstIndex++] = rgba32[srcIndex++]; // G
            rgb24[dstIndex++] = rgba32[srcIndex++]; // B
            srcIndex++; // Skip A
        }

        return rgb24;
    }
}
