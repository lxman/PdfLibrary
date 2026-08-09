using System.Text;

namespace PdfLibrary.Fonts;

/// <summary>
/// Serialises a character-code → text map to a <c>/ToUnicode</c> CMap stream body
/// (ISO 32000-1, 9.10.3). The counterpart to <see cref="ToUnicodeCMap.Parse"/>, and pinned against
/// it by a round-trip test: a writer the shipped reader cannot read would produce fixes that report
/// success while preflight still fails.
/// </summary>
public static class ToUnicodeCMapWriter
{
    /// <summary>ISO 32000-1 9.10.3: a bfchar section holds at most 100 entries.</summary>
    private const int MaxEntriesPerSection = 100;

    public static byte[] Write(IReadOnlyDictionary<int, string> codeToText)
    {
        ArgumentNullException.ThrowIfNull(codeToText);

        // Two-byte codes throughout. A one-byte codespace would be smaller for simple fonts, but
        // two bytes is correct for both simple and composite fonts and the reader accepts it either
        // way — one form is worth less than one code path.
        var sb = new StringBuilder();
        sb.Append("/CIDInit /ProcSet findresource begin\n");
        sb.Append("12 dict begin\n");
        sb.Append("begincmap\n");
        sb.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
        sb.Append("/CMapName /Adobe-Identity-UCS def\n");
        sb.Append("/CMapType 2 def\n");
        sb.Append("1 begincodespacerange\n");
        sb.Append("<0000> <FFFF>\n");
        sb.Append("endcodespacerange\n");

        List<KeyValuePair<int, string>> entries = codeToText
            .Where(kv => kv.Value.Length > 0)
            .OrderBy(kv => kv.Key)
            .ToList();

        for (var offset = 0; offset < entries.Count; offset += MaxEntriesPerSection)
        {
            List<KeyValuePair<int, string>> chunk =
                entries.Skip(offset).Take(MaxEntriesPerSection).ToList();

            sb.Append(chunk.Count).Append(" beginbfchar\n");
            foreach ((int code, string text) in chunk)
                sb.Append('<').Append(code.ToString("X4")).Append("> <")
                  .Append(Utf16BeHex(text)).Append(">\n");
            sb.Append("endbfchar\n");
        }

        sb.Append("endcmap\n");
        sb.Append("CMapName currentdict /CMap defineresource pop\n");
        sb.Append("end\n");
        sb.Append("end\n");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>
    /// The destination of a bfchar entry is a UTF-16BE string, so a non-BMP character is written as
    /// its surrogate pair and a ligature as consecutive code units. Encoding char-by-char would
    /// corrupt both.
    /// </summary>
    private static string Utf16BeHex(string text)
    {
        var sb = new StringBuilder(text.Length * 4);
        foreach (char unit in text)
            sb.Append(((int)unit).ToString("X4"));
        return sb.ToString();
    }
}
