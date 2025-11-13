using Melville.JBig2;
using Melville.JBig2.BinaryBitmaps;

namespace PdfLibrary.Filters;

/// <summary>
/// JBIG2Decode filter - JBIG2 compression for bi-level images (ISO 32000-1:2008 section 7.4.9)
/// Uses Melville.JBig2 for decoding
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

        try
        {
            // Handle JBIG2Globals parameter if present (shared data across multiple JBIG2 streams)
            byte[]? globals = null;
            if (parameters?.TryGetValue("JBIG2Globals", out object? globalsObj) == true)
            {
                globals = globalsObj as byte[];
            }

            // Use Melville.JBig2 decoder
            var reader = new JbigExplicitPageReader();
            reader.RequestPage(1);

            // Process globals first if present
            if (globals is { Length: > 0 })
            {
                using var globalsStream = new MemoryStream(globals);
                reader.ProcessSequentialSegmentsAsync(globalsStream, 1).GetAwaiter().GetResult();
            }

            // Process main data
            using (var dataStream = new MemoryStream(data))
            {
                reader.ProcessSequentialSegmentsAsync(dataStream, 1).GetAwaiter().GetResult();
            }

            // Get the decoded page
            IJBigBitmap page = reader.GetPage(1);
            (byte[] buffer, BitOffset bufferLength) = page.ColumnLocation(0);

            // JBIG2 spec: 0 = white (background), 1 = black (foreground)
            // DeviceGray: 0 = black, 255 = white
            // Melville returns bit-packed data, need to invert bits and expand to bytes
            int length = page.BufferLength();
            var result = new byte[length];
            Array.Copy(buffer, result, length);

            // Invert all bits (JBIG2 has opposite convention from PDF DeviceGray)
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
