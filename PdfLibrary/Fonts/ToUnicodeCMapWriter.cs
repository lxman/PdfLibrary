using System.Text;

namespace PdfLibrary.Fonts;

/// <summary>Width of character codes the /ToUnicode CMap's codespace declares.</summary>
public enum ToUnicodeCodespace
{
    /// <summary>Simple fonts: ISO 32000-1/2 §9.10.3 requires a one-byte codespace.</summary>
    OneByte,

    /// <summary>CID-keyed / composite fonts.</summary>
    TwoByte,
}

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

    /// <summary>
    /// <paramref name="codespace"/> has no default, deliberately: ISO 32000-1/2 §9.10.3 requires the
    /// codespace to match the font's actual encoding ("for a simple font, the codespace shall be one
    /// byte long"). Inferring the width from the codes present would be wrong for a CID-keyed font
    /// whose CIDs all happen to be under 256 — the caller must say.
    /// </summary>
    public static byte[] Write(IReadOnlyDictionary<int, string> codeToText, ToUnicodeCodespace codespace)
    {
        ArgumentNullException.ThrowIfNull(codeToText);

        foreach ((int code, string text) in codeToText)
            if (text.Length == 0)
                throw new ArgumentException(
                    $"Code {code} maps to an empty string. A ToUnicode entry must map to at least "
                    + "one character; a code with no honest mapping must be omitted by the caller, "
                    + "not passed through as empty.", nameof(codeToText));

        (string codespaceLow, string codespaceHigh, string codeFormat) = codespace switch
        {
            ToUnicodeCodespace.OneByte => ("<00>", "<FF>", "X2"),
            ToUnicodeCodespace.TwoByte => ("<0000>", "<FFFF>", "X4"),
            _ => throw new ArgumentOutOfRangeException(nameof(codespace)),
        };

        var sb = new StringBuilder();
        sb.Append("/CIDInit /ProcSet findresource begin\n");
        sb.Append("12 dict begin\n");
        sb.Append("begincmap\n");
        sb.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
        sb.Append("/CMapName /Adobe-Identity-UCS def\n");
        sb.Append("/CMapType 2 def\n");
        sb.Append("1 begincodespacerange\n");
        sb.Append(codespaceLow).Append(' ').Append(codespaceHigh).Append('\n');
        sb.Append("endcodespacerange\n");

        List<KeyValuePair<int, string>> entries = codeToText
            .OrderBy(kv => kv.Key)
            .ToList();

        for (var offset = 0; offset < entries.Count; offset += MaxEntriesPerSection)
        {
            List<KeyValuePair<int, string>> chunk =
                entries.Skip(offset).Take(MaxEntriesPerSection).ToList();

            sb.Append(chunk.Count).Append(" beginbfchar\n");
            foreach ((int code, string text) in chunk)
                sb.Append('<').Append(code.ToString(codeFormat)).Append("> <")
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
