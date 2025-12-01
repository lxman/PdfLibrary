namespace PdfLibrary.Filters;

/// <summary>
/// RunLengthDecode filter - simple run-length compression (ISO 32000-1:2008 section 7.4.5)
/// Used for compressing data with repeating sequences
/// </summary>
internal class RunLengthDecodeFilter : IStreamFilter
{
    public string Name => "RunLengthDecode";

    public byte[] Encode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        using var output = new MemoryStream();
        var i = 0;

        while (i < data.Length)
        {
            // Look for runs of identical bytes
            var runLength = 1;
            while (i + runLength < data.Length &&
                   data[i] == data[i + runLength] &&
                   runLength < 128)
            {
                runLength++;
            }

            // If we have a run of 2 or more, encode as run
            if (runLength >= 2)
            {
                output.WriteByte((byte)(257 - runLength));
                output.WriteByte(data[i]);
                i += runLength;
            }
            else
            {
                // Find literal sequence (no repeating bytes)
                int literalStart = i;
                var literalLength = 0;

                while (i < data.Length && literalLength < 128)
                {
                    // Check if next bytes form a run
                    var nextRunLength = 1;
                    while (i + nextRunLength < data.Length &&
                           data[i] == data[i + nextRunLength] &&
                           nextRunLength < 3)
                    {
                        nextRunLength++;
                    }

                    // If we found a run, stop the literal sequence
                    if (nextRunLength >= 3)
                        break;

                    i++;
                    literalLength++;
                }

                // Write literal sequence
                output.WriteByte((byte)(literalLength - 1));
                output.Write(data, literalStart, literalLength);
            }
        }

        // Write end-of-data marker
        output.WriteByte(128);

        return output.ToArray();
    }

    public byte[] Decode(byte[] data)
    {
        return Decode(data, null);
    }

    public byte[] Decode(byte[] data, Dictionary<string, object>? parameters)
    {
        ArgumentNullException.ThrowIfNull(data);

        using var output = new MemoryStream();
        var i = 0;

        while (i < data.Length)
        {
            byte length = data[i++];

            // Check for end-of-data marker
            if (length == 128)
                break;

            if (length <= 127)
            {
                // Copy the next length+1 bytes literally
                int literalCount = length + 1;

                // Ensure we don't read past the end of data
                if (i + literalCount > data.Length)
                    literalCount = data.Length - i;

                output.Write(data, i, literalCount);
                i += literalCount;
            }
            else // length is 129-255
            {
                // Repeat the next byte (257 - length) times
                int repeatCount = 257 - length;

                if (i >= data.Length)
                    break;

                byte repeatByte = data[i++];

                for (var j = 0; j < repeatCount; j++)
                {
                    output.WriteByte(repeatByte);
                }
            }
        }

        return output.ToArray();
    }
}
