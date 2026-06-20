using System.Text;

namespace PdfLibrary.Core.Primitives;

/// <summary>
/// PDFDocEncoding ⇄ Unicode codec for PDF text strings (ISO 32000-2 §7.9.2, Annex D.3).
/// Encodes to single-byte PDFDocEncoding when every character is representable, otherwise to
/// UTF-16BE with a leading FE FF byte-order mark. Decoding sniffs the BOM (FE FF → UTF-16BE,
/// EF BB BF → UTF-8 for PDF 2.0 inputs) and otherwise decodes PDFDocEncoding.
/// This is for TEXT strings only — byte strings (the /ID, content-stream show-strings, encryption
/// values, name-tree keys) must NOT pass through here.
/// </summary>
internal static class PdfDocEncoding
{
    private static readonly (byte B, char C)[] Special =
    {
        (0x18, '˘'), (0x19, 'ˇ'), (0x1A, 'ˆ'), (0x1B, '˙'),
        (0x1C, '˝'), (0x1D, '˛'), (0x1E, '˚'), (0x1F, '˜'),
        (0x80, '•'), (0x81, '†'), (0x82, '‡'), (0x83, '…'),
        (0x84, '—'), (0x85, '–'), (0x86, 'ƒ'), (0x87, '⁄'),
        (0x88, '‹'), (0x89, '›'), (0x8A, '−'), (0x8B, '‰'),
        (0x8C, '„'), (0x8D, '“'), (0x8E, '”'), (0x8F, '‘'),
        (0x90, '’'), (0x91, '‚'), (0x92, '™'), (0x93, 'ﬁ'),
        (0x94, 'ﬂ'), (0x95, 'Ł'), (0x96, 'Œ'), (0x97, 'Š'),
        (0x98, 'Ÿ'), (0x99, 'Ž'), (0x9A, 'ı'), (0x9B, 'ł'),
        (0x9C, 'œ'), (0x9D, 'š'), (0x9E, 'ž'), (0xA0, '€'),
    };
    private static readonly byte[] UndefinedBytes = { 0x7F, 0x9F };

    private const char UndefinedMarker = '￿';

    private static readonly char[] ByteToChar = BuildByteToChar();
    private static readonly Dictionary<char, byte> CharToByte = BuildCharToByte();

    private static char[] BuildByteToChar()
    {
        var map = new char[256];
        for (var i = 0; i < 256; i++) map[i] = (char)i;
        foreach ((byte b, char c) in Special) map[b] = c;
        foreach (byte b in UndefinedBytes) map[b] = UndefinedMarker;
        return map;
    }

    private static Dictionary<char, byte> BuildCharToByte()
    {
        var map = new Dictionary<char, byte>(256);
        for (var i = 0; i < 256; i++)
        {
            char c = ByteToChar[i];
            if (c != UndefinedMarker) map.TryAdd(c, (byte)i);
        }
        return map;
    }

    public static bool IsRepresentable(string text)
    {
        foreach (char c in text)
            if (!CharToByte.ContainsKey(c)) return false;
        return true;
    }

    public static byte[] Encode(string text)
    {
        if (IsRepresentable(text))
        {
            var bytes = new byte[text.Length];
            for (var i = 0; i < text.Length; i++) bytes[i] = CharToByte[text[i]];

            // If the single-byte representation starts with a BOM-like marker (FE FF or EF BB BF),
            // Decode would mis-sniff it as UTF-16BE or UTF-8. Fall through to UTF-16BE instead so
            // the leading FE FF in the output is unambiguously a real BOM.
            bool startsWithUtf16Bom = bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF;
            bool startsWithUtf8Bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            if (!startsWithUtf16Bom && !startsWithUtf8Bom)
                return bytes;
        }

        byte[] utf16 = Encoding.BigEndianUnicode.GetBytes(text);
        var withBom = new byte[utf16.Length + 2];
        withBom[0] = 0xFE;
        withBom[1] = 0xFF;
        Array.Copy(utf16, 0, withBom, 2, utf16.Length);
        return withBom;
    }

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes[3..]);

        var sb = new StringBuilder(bytes.Length);
        foreach (byte b in bytes)
        {
            char c = ByteToChar[b];
            sb.Append(c == UndefinedMarker ? '�' : c);
        }
        return sb.ToString();
    }
}
