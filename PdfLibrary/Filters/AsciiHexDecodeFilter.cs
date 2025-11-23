using System.Text;

namespace PdfLibrary.Filters;

/// <summary>
/// ASCIIHexDecode filter - encodes binary data as ASCII hexadecimal (ISO 32000-1:2008 section 7.4.2)
/// Each byte is represented by two hexadecimal digits (0-9, A-F)
/// The sequence ends with EOD marker (>)
/// </summary>
public class AsciiHexDecodeFilter : IStreamFilter
{
    public string Name => "ASCIIHexDecode";

    public byte[] Encode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var sb = new StringBuilder(data.Length * 2 + 1);

        foreach (byte b in data)
        {
            sb.Append($"{b:X2}");
        }

        sb.Append('>'); // EOD marker

        return Encoding.ASCII.GetBytes(sb.ToString());
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
            var ch = (char)data[i++];

            // Skip whitespace
            if (char.IsWhiteSpace(ch))
                continue;

            // Check for EOD marker
            if (ch == '>')
                break;

            // First hex digit
            if (!IsHexDigit(ch))
                throw new InvalidDataException($"Invalid hex character: {ch}");

            int high = HexValue(ch);

            // Second hex digit (optional - if missing, assume 0)
            var low = 0;
            if (i < data.Length)
            {
                var ch2 = (char)data[i];

                // Skip whitespace
                while (char.IsWhiteSpace(ch2) && i < data.Length - 1)
                {
                    i++;
                    ch2 = (char)data[i];
                }

                if (ch2 != '>' && IsHexDigit(ch2))
                {
                    low = HexValue(ch2);
                    i++;
                }
            }

            var value = (byte)((high << 4) | low);
            output.WriteByte(value);
        }

        return output.ToArray();
    }

    private static bool IsHexDigit(char ch) =>
        ch is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f';

    private static int HexValue(char ch)
    {
        if (ch is >= '0' and <= '9')
            return ch - '0';
        if (ch is >= 'A' and <= 'F')
            return ch - 'A' + 10;
        if (ch is >= 'a' and <= 'f')
            return ch - 'a' + 10;
        throw new ArgumentException($"Invalid hex character: {ch}");
    }
}
