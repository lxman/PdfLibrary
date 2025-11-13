using PdfLibrary.Filters.Lzw;

namespace PdfLibrary.Filters;

/// <summary>
/// LZWDecode filter - Lempel-Ziv-Welch compression (ISO 32000-1:2008 section 7.4.4)
/// Dictionary-based compression commonly used in TIFF and older PDF files
/// Uses OcfLzw2 implementation (Apache 2.0 license) - tested against 10+ million real-world LZW blobs
/// </summary>
public class LzwDecodeFilter : IStreamFilter
{
    public string Name => "LZWDecode";

    public byte[] Encode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        // LZW encoding is not commonly needed for PDF reading
        // OcfLzw2 is a decode-only implementation
        throw new NotSupportedException(
            "LZW encoding is not supported.");
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
            // Use OcfLzw2 for decoding - battle-tested implementation
            // Parameters like EarlyChange are handled internally by OcfLzw2
            return OcfLzw2.Decode(data);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to decode LZW data: {ex.Message}", ex);
        }
    }
}
