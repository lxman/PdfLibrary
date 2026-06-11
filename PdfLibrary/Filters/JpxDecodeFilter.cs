using Jp2Codec;
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
            // Decode JPEG 2000 data. Jpeg2000.Decompress already renders managed colour spaces
            // (sRGB / sYCC / ICC) to 3-channel and greyscale to 1-channel; the filter only sees a
            // 4-component result for a raw codestream whose colour space is Unspecified, where the
            // PDF /ColorSpace entry is the authority on interpretation.
            byte[] imageBytes = Jpeg2000.Decompress(data, out int width, out int height, out int components);

            PdfLogger.Log(LogCategory.Images, $"JPXDecode: {width}x{height}, {components} components, {imageBytes.Length} bytes");

            if (components == 1)
            {
                // Greyscale - replicate to RGB so downstream RGB consumers render it.
                int pixelCount = imageBytes.Length;
                var pixels = new byte[pixelCount * 3];
                var offset = 0;
                for (var i = 0; i < pixelCount; i++)
                {
                    byte gray = imageBytes[i];
                    pixels[offset++] = gray;
                    pixels[offset++] = gray;
                    pixels[offset++] = gray;
                }
                return pixels;
            }

            // 3-component (already RGB), 4-component, or other: return the raw interleaved samples
            // unchanged. We must NOT strip a 4th channel as alpha — a CMYK JPEG 2000 image would lose
            // its black (K) plate and render as garbage. PDF carries soft-mask transparency via /SMask,
            // not an inline 4th channel, so a 4-component JPX is CMYK in practice; the PDF /ColorSpace
            // dictionary tells the consumer how to interpret these samples.
            return imageBytes;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to decode JPEG 2000 data: {ex.Message}", ex);
        }
    }
}
