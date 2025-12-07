using Compressors.Jpeg2000;
using Logging;

namespace PdfLibrary.Filters;

/// <summary>
/// JPXDecode filter - JPEG 2000 compression (ISO 32000-1:2008 section 7.4.10)
/// </summary>
internal class JpxDecodeFilter : IStreamFilter
{
    public string Name => "JPXDecode";

    public byte[] Encode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        throw new NotSupportedException("JPEG 2000 encoding is not supported.");
    }

    public byte[] Decode(byte[] data)
    {
        return Decode(data, null);
    }

    public byte[] Decode(byte[] data, Dictionary<string, object>? parameters)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length == 0)
            return [];

        try
        {
            // Decode JPEG 2000 data
            byte[] imageBytes = Jpeg2000.Decompress(data, out int width, out int height, out int components);

            PdfLogger.Log(LogCategory.Images, $"JPXDecode: Decoded {width}x{height}, {components} components, {imageBytes.Length} bytes");

            // Debug: Sample first few pixels to check data
            if (imageBytes.Length >= components * 10)
            {
                var samples = new List<string>();
                for (var i = 0; i < 5; i++)
                {
                    int offset = i * components;
                    if (components == 3)
                        samples.Add($"({imageBytes[offset]},{imageBytes[offset+1]},{imageBytes[offset+2]})");
                    else if (components == 1)
                        samples.Add($"({imageBytes[offset]})");
                }
                PdfLogger.Log(LogCategory.Images, $"JPXDecode: First 5 pixels: {string.Join(" ", samples)}");
            }

            // If already RGB, return as-is
            if (components == 3)
            {
                return imageBytes;
            }

            // Calculate dimensions
            int pixelCount = imageBytes.Length / components;
            var pixels = new byte[pixelCount * 3];

            switch (components)
            {
                case 1:
                {
                    // Grayscale - replicate to RGB
                    var offset = 0;
                    for (var i = 0; i < pixelCount; i++)
                    {
                        byte gray = imageBytes[i];
                        pixels[offset++] = gray;
                        pixels[offset++] = gray;
                        pixels[offset++] = gray;
                    }

                    break;
                }
                case 4:
                {
                    // RGBA - strip alpha channel
                    var srcOffset = 0;
                    var dstOffset = 0;
                    for (var i = 0; i < pixelCount; i++)
                    {
                        pixels[dstOffset++] = imageBytes[srcOffset++]; // R
                        pixels[dstOffset++] = imageBytes[srcOffset++]; // G
                        pixels[dstOffset++] = imageBytes[srcOffset++]; // B
                        srcOffset++; // Skip alpha
                    }

                    break;
                }
                default:
                {
                    // Unknown format - return raw bytes and let caller handle it
                    return imageBytes;
                }
            }

            return pixels;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to decode JPEG 2000 data: {ex.Message}", ex);
        }
    }
}
