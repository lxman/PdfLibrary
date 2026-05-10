using Jbig2Decoder;
using Logging;

namespace PdfLibrary.Filters;

/// <summary>
/// JBIG2Decode filter - JBIG2 compression for bi-level images (ISO 32000-1:2008 section 7.4.9)
/// </summary>
internal class Jbig2DecodeFilter : IStreamFilter
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
            // Tolerance mode mirrors Chrome / Acrobat: accept malformed streams
            // (forward references, missing segments) commonly emitted by non-strict producers.
            var decoder = new JBIG2StreamDecoder { TolerateMissingSegments = true };

            // Handle JBIG2Globals parameter if present (shared data across multiple JBIG2 streams)
            if (parameters?.TryGetValue("JBIG2Globals", out object? globalsObj) == true &&
                globalsObj is byte[] { Length: > 0 } globals)
            {
                decoder.SetGlobalData(globals);
            }

            // Decode to 1-bit packed bitmap (1 = black, MSB-first, stride = (width + 7) / 8).
            byte[] result = decoder.DecodeJBIG2ToPacked(data, out int width, out int height);

            int expected = ((width + 7) / 8) * height;
            if (result.Length == 0 || width <= 0 || height <= 0 || result.Length != expected)
                return [];

            // JBIG2 spec: 0 = white (background), 1 = black (foreground).
            // PDF DeviceGray for 1-bit images: 0 = black, 1 = white. Invert for PDF compatibility.
            for (var i = 0; i < result.Length; i++)
                result[i] = (byte)~result[i];

            return result;
        }
        catch (Exception ex) when (ex is NullReferenceException or IndexOutOfRangeException)
        {
            // Malformed JBIG2 streams can throw these from deep in the decoder.
            // Match the prior wrapper's policy of returning empty rather than propagating.
            PdfLogger.Log(LogCategory.Images, $"JBIG2 decode failed on malformed stream ({data.Length} bytes): {ex.GetType().Name}: {ex.Message}");
            return [];
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to decode JBIG2 data: {ex.Message}", ex);
        }
    }
}
