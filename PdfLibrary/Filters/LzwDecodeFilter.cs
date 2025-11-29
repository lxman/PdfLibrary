using Compressors.Lzw;

namespace PdfLibrary.Filters;

/// <summary>
/// LZWDecode filter - Lempel-Ziv-Welch compression (ISO 32000-1:2008 section 7.4.4)
/// Dictionary-based compression commonly used in TIFF and older PDF files
/// </summary>
internal class LzwDecodeFilter : IStreamFilter
{
    public string Name => "LZWDecode";

    public byte[] Encode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Lzw.Compress(data);
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
            // Check for EarlyChange parameter (PDF default is 1/true)
            var earlyChange = true;
            if (parameters?.TryGetValue("EarlyChange", out object? ecValue) == true)
            {
                earlyChange = ecValue switch
                {
                    bool b => b,
                    int i => i != 0,
                    long l => l != 0,
                    _ => true
                };
            }

            var options = new LzwOptions
            {
                EarlyChange = earlyChange
            };

            return Lzw.Decompress(data, options);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to decode LZW data: {ex.Message}", ex);
        }
    }
}
