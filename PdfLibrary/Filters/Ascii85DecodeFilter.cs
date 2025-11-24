using System.Text;

namespace PdfLibrary.Filters;

/// <summary>
/// ASCII85Decode filter - encodes binary data as ASCII base-85 (ISO 32000-1:2008 section 7.4.3)
/// More efficient than ASCIIHexDecode (4 bytes encoded as 5 ASCII characters)
/// The sequence ends with ~> marker
/// </summary>
public class Ascii85DecodeFilter : IStreamFilter
{
    public string Name => "ASCII85Decode";

    public byte[] Encode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var sb = new StringBuilder();
        var count = 0;
        uint tuple = 0;

        foreach (byte b in data)
        {
            tuple = (tuple << 8) | b;
            count++;

            if (count != 4) continue;
            EncodeBlock(sb, tuple, 5);
            tuple = 0;
            count = 0;
        }

        // Handle remaining bytes
        if (count > 0)
        {
            // Pad with zeros
            tuple <<= 8 * (4 - count);
            EncodeBlock(sb, tuple, count + 1);
        }

        sb.Append("~>"); // EOD marker

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
        uint tuple = 0;
        var count = 0;

        for (var i = 0; i < data.Length; i++)
        {
            var ch = (char)data[i];

            // Skip whitespace
            if (char.IsWhiteSpace(ch))
                continue;

            // Check for EOD marker
            if (ch == '~')
            {
                if (i + 1 < data.Length && data[i + 1] == '>')
                    break;
            }

            switch (ch)
            {
                // Check for 'z' (shorthand for 0x00000000)
                case 'z' when count != 0:
                    throw new InvalidDataException("'z' can only appear at tuple boundary");
                case 'z':
                    output.Write([0, 0, 0, 0], 0, 4);
                    continue;
                // Valid characters are ! through u (33-117)
                case < '!' or > 'u':
                    throw new InvalidDataException($"Invalid ASCII85 character: {ch}");
            }

            tuple = tuple * 85 + ((uint)ch - 33);
            count++;

            if (count != 5) continue;
            // Output 4 bytes
            output.WriteByte((byte)(tuple >> 24));
            output.WriteByte((byte)(tuple >> 16));
            output.WriteByte((byte)(tuple >> 8));
            output.WriteByte((byte)tuple);

            tuple = 0;
            count = 0;
        }

        // Handle remaining characters
        if (count <= 0) return output.ToArray();
        {
            // Add padding
            for (int i = count; i < 5; i++)
            {
                tuple = tuple * 85 + 84; // 'u' - 33 = 84
            }

            // Output count-1 bytes
            for (var i = 0; i < count - 1; i++)
            {
                output.WriteByte((byte)(tuple >> (24 - 8 * i)));
            }
        }

        return output.ToArray();
    }

    private static void EncodeBlock(StringBuilder sb, uint tuple, int count)
    {
        // Special case: all zeros
        if (tuple == 0 && count == 5)
        {
            sb.Append('z');
            return;
        }

        var chars = new char[5];
        for (var i = 4; i >= 0; i--)
        {
            chars[i] = (char)((tuple % 85) + 33);
            tuple /= 85;
        }

        sb.Append(chars, 0, count);
    }
}
