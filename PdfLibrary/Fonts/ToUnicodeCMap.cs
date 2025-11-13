using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PdfLibrary.Fonts;

/// <summary>
/// Represents a ToUnicode CMap that maps character codes to Unicode
/// (ISO 32000-1:2008 section 9.10.3)
/// </summary>
public partial class ToUnicodeCMap
{
    private readonly Dictionary<int, string> _charToUnicode = new();

    /// <summary>
    /// Looks up the Unicode string for a character code
    /// </summary>
    public string? Lookup(int charCode)
    {
        return _charToUnicode.GetValueOrDefault(charCode);
    }

    /// <summary>
    /// Parses a ToUnicode CMap from stream data
    /// </summary>
    public static ToUnicodeCMap Parse(byte[] data)
    {
        var cmap = new ToUnicodeCMap();
        string content = Encoding.ASCII.GetString(data);

        // Parse bfchar mappings (single character mappings)
        ParseBfChar(cmap, content);

        // Parse bfrange mappings (range mappings)
        ParseBfRange(cmap, content);

        return cmap;
    }

    // Regex for bfchar: <charcode> <unicode>
    [GeneratedRegex(@"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>")]
    private static partial Regex BfCharRegex();

    // Regex for bfrange: <start> <end> <unicode_start> or <start> <end> [<unicode> ...]
    [GeneratedRegex(@"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>")]
    private static partial Regex BfRangeSimpleRegex();

    [GeneratedRegex(@"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*\[((?:\s*<[0-9A-Fa-f]+>)+)\s*\]")]
    private static partial Regex BfRangeArrayRegex();

    /// <summary>
    /// Parses bfchar mappings from CMap content
    /// Format: beginbfchar ... endbfchar
    /// </summary>
    private static void ParseBfChar(ToUnicodeCMap cmap, string content)
    {
        // Find all bfchar blocks
        List<string> bfcharBlocks = FindBlocks(content, "beginbfchar", "endbfchar");

        foreach (string block in bfcharBlocks)
        {
            MatchCollection matches = BfCharRegex().Matches(block);
            foreach (Match match in matches)
            {
                if (match.Groups.Count < 3) continue;
                string charCodeHex = match.Groups[1].Value;
                string unicodeHex = match.Groups[2].Value;

                if (!int.TryParse(charCodeHex, NumberStyles.HexNumber, null, out int charCode)) continue;
                string unicode = HexToUnicode(unicodeHex);
                cmap._charToUnicode[charCode] = unicode;
            }
        }
    }

    /// <summary>
    /// Parses bfrange mappings from CMap content
    /// Format: beginbfrange ... endbfrange
    /// </summary>
    private static void ParseBfRange(ToUnicodeCMap cmap, string content)
    {
        List<string> bfrangeBlocks = FindBlocks(content, "beginbfrange", "endbfrange");

        foreach (string block in bfrangeBlocks)
        {
            // Try simple range format: <start> <end> <unicode_start>
            MatchCollection simpleMatches = BfRangeSimpleRegex().Matches(block);
            foreach (Match match in simpleMatches)
            {
                if (match.Groups.Count < 4) continue;
                if (!int.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, null, out int start) ||
                    !int.TryParse(match.Groups[2].Value, NumberStyles.HexNumber, null, out int end) ||
                    !int.TryParse(match.Groups[3].Value, NumberStyles.HexNumber, null, out int unicodeStart))
                    continue;
                for (int charCode = start; charCode <= end; charCode++)
                {
                    int unicodeValue = unicodeStart + (charCode - start);
                    cmap._charToUnicode[charCode] = char.ConvertFromUtf32(unicodeValue);
                }
            }

            // Try array format: <start> <end> [<unicode1> <unicode2> ...]
            MatchCollection arrayMatches = BfRangeArrayRegex().Matches(block);
            foreach (Match match in arrayMatches)
            {
                if (match.Groups.Count < 4) continue;
                if (!int.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, null, out int start) ||
                    !int.TryParse(match.Groups[2].Value, NumberStyles.HexNumber, null, out int end)) continue;
                string arrayContent = match.Groups[3].Value;
                MatchCollection unicodeMatches = Regex.Matches(arrayContent, @"<([0-9A-Fa-f]+)>");

                int charCode = start;
                foreach (Match unicodeMatch in unicodeMatches)
                {
                    if (charCode > end || unicodeMatch.Groups.Count < 2) continue;
                    string unicode = HexToUnicode(unicodeMatch.Groups[1].Value);
                    cmap._charToUnicode[charCode] = unicode;
                    charCode++;
                }
            }
        }
    }

    /// <summary>
    /// Finds all blocks between begin and end markers
    /// </summary>
    private static List<string> FindBlocks(string content, string beginMarker, string endMarker)
    {
        var blocks = new List<string>();
        var pos = 0;

        while (true)
        {
            int beginPos = content.IndexOf(beginMarker, pos, StringComparison.Ordinal);
            if (beginPos == -1)
                break;

            int endPos = content.IndexOf(endMarker, beginPos, StringComparison.Ordinal);
            if (endPos == -1)
                break;

            int blockStart = beginPos + beginMarker.Length;
            int blockLength = endPos - blockStart;
            blocks.Add(content.Substring(blockStart, blockLength));

            pos = endPos + endMarker.Length;
        }

        return blocks;
    }

    /// <summary>
    /// Converts hex string to Unicode string
    /// Handles both 16-bit (4 hex digits) and 32-bit (8 hex digits) Unicode
    /// </summary>
    private static string HexToUnicode(string hex)
    {
        // Remove any whitespace
        hex = hex.Trim();

        switch (hex.Length)
        {
            // Handle multi-byte sequences (e.g., UTF-16 surrogate pairs)
            // Try as single 32-bit code point
            case 8 when int.TryParse(hex, NumberStyles.HexNumber, null, out int codePoint):
                return char.ConvertFromUtf32(codePoint);
            // Try as two 16-bit values (UTF-16 surrogate pair)
            case 8 when int.TryParse(hex[..4], NumberStyles.HexNumber, null, out int high) &&
                        int.TryParse(hex[4..], NumberStyles.HexNumber, null, out int low):
                return new string([(char)high, (char)low]);
            case 8:
                break;
            case 4:
            {
                // Single 16-bit value
                if (int.TryParse(hex, NumberStyles.HexNumber, null, out int value))
                {
                    return char.ConvertFromUtf32(value);
                }

                break;
            }
            default:
            {
                if (hex.Length % 4 == 0)
                {
                    // Multiple characters
                    var sb = new StringBuilder();
                    for (var i = 0; i < hex.Length; i += 4)
                    {
                        if (int.TryParse(hex.AsSpan(i, 4), NumberStyles.HexNumber, null, out int value))
                        {
                            sb.Append(char.ConvertFromUtf32(value));
                        }
                    }
                    return sb.ToString();
                }

                break;
            }
        }

        return "?";
    }
}
