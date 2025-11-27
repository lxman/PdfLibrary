using Compressors.Ccitt;
using Logging;

namespace PdfLibrary.Filters;

/// <summary>
/// CCITTFaxDecode filter - CCITT Group 3/4 fax compression (ISO 32000-1:2008 section 7.4.7)
/// Used for bi-level (black and white) images
/// </summary>
public class CcittFaxDecodeFilter : IStreamFilter
{
    public string Name => "CCITTFaxDecode";

    public byte[] Encode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        throw new NotSupportedException("CCITT Fax encoding is not supported.");
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
            // CCITT decoding requires several parameters from the PDF:
            // - K: encoding type (-1 = Group 4, 0 = Group 3 1-D, >0 = Group 3 2-D)
            // - Columns: width of the image
            // - Rows: height of the image (optional)
            // - EncodedByteAlign: whether rows are byte-aligned
            // - BlackIs1: whether 1 bits represent black or white
            // - EndOfLine: whether EOL markers are present
            // - EndOfBlock: whether end-of-block marker is expected

            int k = GetIntParameter(parameters, "K", 0);
            int columns = GetIntParameter(parameters, "Columns", 1728); // Default fax width
            int rows = GetIntParameter(parameters, "Rows", 0);
            bool encodedByteAlign = GetBoolParameter(parameters, "EncodedByteAlign", false);
            bool blackIs1 = GetBoolParameter(parameters, "BlackIs1", false);
            bool endOfLine = GetBoolParameter(parameters, "EndOfLine", false);
            bool endOfBlock = GetBoolParameter(parameters, "EndOfBlock", true);

            PdfLogger.Log(LogCategory.Images, $"  [CCITT] K={k}, Columns={columns}, Rows={rows}, BlackIs1={blackIs1}, EncodedByteAlign={encodedByteAlign}, EndOfLine={endOfLine}, EndOfBlock={endOfBlock}");
            PdfLogger.Log(LogCategory.Images, $"  [CCITT] Input: {data.Length} bytes, Expected output: {(columns * rows + 7) / 8} bytes");

            byte[] result = Ccitt.Decompress(data, k, columns, rows, blackIs1, encodedByteAlign, endOfLine, endOfBlock);

            PdfLogger.Log(LogCategory.Images, $"  [CCITT] Output: {result.Length} bytes, ActualRows={result.Length * 8 / columns}");

            return result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to decode CCITT Fax data: {ex.Message}", ex);
        }
    }

    private static int GetIntParameter(Dictionary<string, object>? parameters, string key, int defaultValue)
    {
        if (parameters?.TryGetValue(key, out object? value) != true)
            return defaultValue;

        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            _ => defaultValue
        };
    }

    private static bool GetBoolParameter(Dictionary<string, object>? parameters, string key, bool defaultValue)
    {
        if (parameters?.TryGetValue(key, out object? value) != true)
            return defaultValue;

        return value switch
        {
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            _ => defaultValue
        };
    }
}
