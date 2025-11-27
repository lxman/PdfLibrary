using Compressors.Jbig2;

namespace PdfLibrary.Filters;

/// <summary>
/// JBIG2Decode filter - JBIG2 compression for bi-level images (ISO 32000-1:2008 section 7.4.9)
/// </summary>
public class Jbig2DecodeFilter : IStreamFilter
{
    public string Name => "JBIG2Decode";

    public byte[] Encode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        throw new NotSupportedException("JBIG2 encoding is not supported.");
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
            // Handle JBIG2Globals parameter if present (shared data across multiple JBIG2 streams)
            byte[]? globals = null;
            if (parameters?.TryGetValue("JBIG2Globals", out object? globalsObj) == true)
            {
                globals = globalsObj as byte[];
            }

            // Decode to 1-bit bitmap (black=1, white=0)
            byte[] result = Jbig2.DecompressToBitmap(data, globals, out _, out _);

            // JBIG2 spec: 0 = white (background), 1 = black (foreground)
            // PDF DeviceGray for 1-bit images: 0 = black, 1 = white
            // Need to invert all bits for PDF compatibility
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = (byte)~result[i];
            }

            return result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to decode JBIG2 data: {ex.Message}", ex);
        }
    }
}
