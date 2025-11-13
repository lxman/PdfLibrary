using System.Buffers;
using Melville.CCITT;
using Melville.Parsing.StreamFilters;

namespace PdfLibrary.Filters;

/// <summary>
/// CCITTFaxDecode filter - CCITT Group 3/4 fax compression (ISO 32000-1:2008 section 7.4.7)
/// Used for bi-level (black and white) images
/// Uses Melville.CCITT for CCITT Type 3 and Type 4 decoding
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

        try
        {
            // CCITT decoding requires several parameters from the PDF:
            // - K: encoding type (-1 = Group 4, 0 = Group 3 1-D, >0 = Group 3 2-D)
            // - Columns: width of the image
            // - Rows: height of the image (optional)
            // - EncodedByteAlign: whether rows are byte-aligned
            // - BlackIs1: whether 1 bits represent black or white

            int k = GetParameter(parameters, "K", 0);
            int columns = GetParameter(parameters, "Columns", 1728); // Default fax width
            int rows = GetParameter(parameters, "Rows", 0);
            bool encodedByteAlign = GetParameter(parameters, "EncodedByteAlign", false);
            bool blackIs1 = GetParameter(parameters, "BlackIs1", false);

            // Create CCITT parameters for Melville decoder
            var ccittParams = new CcittParameters(
                k,
                encodedByteAlign,
                columns,
                rows > 0 ? rows : int.MaxValue, // If rows not specified, decode until EOL
                blackIs1,
                true // endOfLine - PDF default
            );

            // Decode using Melville.CCITT with timeout to prevent hanging on invalid data
            IStreamFilterDefinition decoder = CcittCodecFactory.SelectDecoder(ccittParams);

            // Run decoding in a Task with timeout
            Task<byte[]> decodeTask = Task.Run(() =>
            {
                var sequence = new ReadOnlySequence<byte>(data);
                var reader = new SequenceReader<byte>(sequence);
                var output = new List<byte>();
                var buffer = new byte[Math.Max(decoder.MinWriteSize, 4096)];

                var iterationCount = 0;
                const int maxIterations = 100000;
                long lastConsumed = 0;

                while (!reader.End && iterationCount < maxIterations)
                {
                    (SequencePosition _, int bytesWritten, bool done) = decoder.Convert(ref reader, buffer);
                    iterationCount++;

                    if (bytesWritten > 0)
                    {
                        output.AddRange(buffer.AsSpan(0, bytesWritten).ToArray());
                    }

                    if (done)
                        break;

                    long currentConsumed = reader.Consumed;
                    if (currentConsumed == lastConsumed && bytesWritten == 0)
                    {
                        break;
                    }
                    lastConsumed = currentConsumed;
                }

                (_, int finalBytes, _) = decoder.FinalConvert(ref reader, buffer);
                if (finalBytes > 0)
                {
                    output.AddRange(buffer.AsSpan(0, finalBytes).ToArray());
                }

                return output.ToArray();
            });

            // Calculate timeout based on input size
            // Assume minimum 100KB/sec decoding speed, with 500ms base and 10s max
            int timeoutMs = Math.Max(500, Math.Min(10000, data.Length / 100 + 500));

            if (!decodeTask.Wait(TimeSpan.FromMilliseconds(timeoutMs)))
            {
                throw new InvalidOperationException($"CCITT decoding timed out after {timeoutMs}ms - invalid or unsupported data format");
            }

            if (decodeTask is { IsFaulted: true, Exception: not null })
            {
                throw new InvalidOperationException($"CCITT decoding failed: {decodeTask.Exception.GetBaseException().Message}", decodeTask.Exception.GetBaseException());
            }

            return decodeTask.Result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to decode CCITT Fax data: {ex.Message}", ex);
        }
    }

    private static T GetParameter<T>(Dictionary<string, object>? parameters, string key, T defaultValue)
    {
        if (parameters?.TryGetValue(key, out object? value) == true && value is T typedValue)
        {
            return typedValue;
        }
        return defaultValue;
    }
}
