using System.IO.Compression;

namespace PdfLibrary.Filters;

/// <summary>
/// FlateDecode filter - uses zlib/deflate compression (ISO 32000-1:2008 section 7.4.4)
/// This is the most commonly used filter in PDF files
/// </summary>
internal class FlateDecodeFilter : IStreamFilter
{
    public string Name => "FlateDecode";

    public byte[] Encode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        using var outputStream = new MemoryStream();
        using (var zlibStream = new ZLibStream(outputStream, CompressionLevel.Optimal))
        {
            zlibStream.Write(data, 0, data.Length);
        }

        return outputStream.ToArray();
    }

    public byte[] Decode(byte[] data)
    {
        return Decode(data, null);
    }

    public byte[] Decode(byte[] data, Dictionary<string, object>? parameters)
    {
        ArgumentNullException.ThrowIfNull(data);

        byte[] decoded;

        // Log the first few bytes for debugging
        var headerHex = data.Length >= 4
            ? $"{data[0]:X2} {data[1]:X2} {data[2]:X2} {data[3]:X2}"
            : "too short";
        Logging.PdfLogger.Log(Logging.LogCategory.PdfTool, $"FlateDecode: {data.Length} bytes, header: {headerHex}");

        try
        {
            // First try ZLibStream (handles zlib header 78 xx)
            using var inputStream = new MemoryStream(data);
            using var zlibStream = new ZLibStream(inputStream, CompressionMode.Decompress);
            using var outputStream = new MemoryStream();
            zlibStream.CopyTo(outputStream);
            decoded = outputStream.ToArray();
        }
        catch (InvalidDataException)
        {
            // Fallback: try raw deflate (no zlib header)
            Logging.PdfLogger.Log(Logging.LogCategory.PdfTool, "FlateDecode: ZLibStream failed, trying raw DeflateStream");
            try
            {
                using var inputStream = new MemoryStream(data);
                using var deflateStream = new DeflateStream(inputStream, CompressionMode.Decompress);
                using var outputStream = new MemoryStream();
                deflateStream.CopyTo(outputStream);
                decoded = outputStream.ToArray();
            }
            catch (InvalidDataException ex2)
            {
                // Try skipping first 2 bytes (zlib header) and use raw deflate
                Logging.PdfLogger.Log(Logging.LogCategory.PdfTool, $"FlateDecode: Raw deflate also failed: {ex2.Message}, trying skip 2 bytes");
                if (data.Length > 2)
                {
                    using var inputStream = new MemoryStream(data, 2, data.Length - 2);
                    using var deflateStream = new DeflateStream(inputStream, CompressionMode.Decompress);
                    using var outputStream = new MemoryStream();
                    deflateStream.CopyTo(outputStream);
                    decoded = outputStream.ToArray();
                }
                else
                {
                    throw;
                }
            }
        }

        // Apply predictor if specified in the decode parameters
        if (parameters is null || !parameters.TryGetValue("Predictor", out var predictorObj)) return decoded;
        if (predictorObj is int predictor and > 1)
        {
            decoded = ApplyPredictor(decoded, predictor, parameters);
        }

        return decoded;
    }

    /// <summary>
    /// Applies predictor functions for improved compression (ISO 32000-1:2008 section 7.4.4.4)
    /// </summary>
    private byte[] ApplyPredictor(byte[] data, int predictor, Dictionary<string, object> parameters)
    {
        // Predictor values:
        // 1 = No prediction
        // 2 = TIFF Predictor 2
        // 10-15 = PNG predictors

        if (predictor == 1)
            return data;

        // Get predictor parameters
        var columns = parameters.TryGetValue("Columns", out var colsObj) && colsObj is int cols ? cols : 1;
        var colors = parameters.TryGetValue("Colors", out var colorsObj) && colorsObj is int c ? c : 1;
        var bitsPerComponent = parameters.TryGetValue("BitsPerComponent", out var bpcObj) && bpcObj is int bpc ? bpc : 8;

        var bytesPerPixel = (colors * bitsPerComponent + 7) / 8;
        var rowLength = (columns * colors * bitsPerComponent + 7) / 8;

        return predictor switch
        {
            >= 10 and <= 15 => ApplyPngPredictor(data, rowLength, bytesPerPixel),
            2 => ApplyTiffPredictor(data, rowLength, bytesPerPixel),
            _ => data
        };
    }

    private byte[] ApplyPngPredictor(byte[] data, int rowLength, int bytesPerPixel)
    {
        using var output = new MemoryStream();
        var pos = 0;
        var previousRow = new byte[rowLength];

        while (pos < data.Length)
        {
            if (pos + rowLength + 1 > data.Length)
                break;

            // Read the PNG predictor type (first byte of each row)
            var predictor = data[pos++];
            var currentRow = new byte[rowLength];

            // Decode row based on predictor
            for (var i = 0; i < rowLength; i++)
            {
                var encoded = data[pos++];
                var left = i >= bytesPerPixel ? currentRow[i - bytesPerPixel] : (byte)0;
                var above = previousRow[i];
                var aboveLeft = i >= bytesPerPixel ? previousRow[i - bytesPerPixel] : (byte)0;

                currentRow[i] = predictor switch
                {
                    0 => encoded, // None
                    1 => (byte)(encoded + left), // Sub
                    2 => (byte)(encoded + above), // Up
                    3 => (byte)(encoded + (left + above) / 2), // Average
                    4 => (byte)(encoded + PaethPredictor(left, above, aboveLeft)), // Paeth
                    _ => encoded
                };
            }

            output.Write(currentRow, 0, rowLength);
            previousRow = currentRow;
        }

        return output.ToArray();
    }

    private byte[] ApplyTiffPredictor(byte[] data, int rowLength, int bytesPerPixel)
    {
        using var output = new MemoryStream();
        var pos = 0;

        while (pos < data.Length)
        {
            if (pos + rowLength > data.Length)
                break;

            var row = new byte[rowLength];

            for (var i = 0; i < rowLength; i++)
            {
                var encoded = data[pos++];
                var left = i >= bytesPerPixel ? row[i - bytesPerPixel] : (byte)0;
                row[i] = (byte)(encoded + left);
            }

            output.Write(row, 0, rowLength);
        }

        return output.ToArray();
    }

    private static byte PaethPredictor(byte a, byte b, byte c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);

        if (pa <= pb && pa <= pc)
            return a;
        return pb <= pc
            ? b
            : c;
    }
}
