using System;
using System.Text;
using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Tags;

/// <summary>
/// ICC v2 §6.5.17 textDescriptionType ('desc'). Carries three parallel descriptions:
/// ASCII (invariant), Unicode (UTF-16BE with a language code), and a fixed-size Macintosh
/// ScriptCode block. Deprecated in v4 (replaced by <see cref="MultiLocalizedUnicodeTagElement"/>)
/// but still ubiquitous in v2 profiles and many v4 profiles in the wild.
///
/// Layout (after the 8-byte type header):
///   uint32  asciiCount        // bytes, including trailing NUL
///   bytes   asciiDescription  // 7-bit ASCII, NUL-terminated
///   uint32  unicodeLanguageCode
///   uint32  unicodeCount      // UTF-16 code units, including trailing NUL
///   bytes   unicodeDescription// UTF-16BE, NUL-terminated
///   uint16  scriptCode
///   uint8   macintoshLength   // bytes used (≤ 67)
///   bytes×67 macintoshDescription
/// </summary>
public sealed class TextDescriptionTagElement : TagElement
{
    public string AsciiDescription { get; }
    public uint UnicodeLanguageCode { get; }
    public string? UnicodeDescription { get; }
    public ushort ScriptCode { get; }
    public string? MacintoshDescription { get; }

    public TextDescriptionTagElement(
        string asciiDescription,
        uint unicodeLanguageCode,
        string? unicodeDescription,
        ushort scriptCode,
        string? macintoshDescription)
        : base(TagTypeSignatures.TextDescription)
    {
        AsciiDescription = asciiDescription;
        UnicodeLanguageCode = unicodeLanguageCode;
        UnicodeDescription = unicodeDescription;
        ScriptCode = scriptCode;
        MacintoshDescription = macintoshDescription;
    }

    internal static TextDescriptionTagElement Parse(IccBinaryReader reader, int payloadBytes)
    {
        if (payloadBytes < 4)
            throw new IccParseException($"textDescription payload {payloadBytes} bytes; need at least 4 for ASCII count.");

        int start = reader.Position;
        int end = start + payloadBytes;

        uint asciiCount = reader.ReadUInt32();
        if (asciiCount > (uint)(end - reader.Position))
            throw new IccParseException($"textDescription ASCII count {asciiCount} exceeds remaining payload.");
        string ascii = ReadAsciiNulTerminated(reader, (int)asciiCount);

        // Unicode section — language code + count + UTF-16BE bytes.
        string? unicode = null;
        uint langCode = 0;
        if (end - reader.Position >= 8)
        {
            langCode = reader.ReadUInt32();
            uint uniCount = reader.ReadUInt32();
            long uniBytes = (long)uniCount * 2L;
            if (uniBytes > end - reader.Position)
                throw new IccParseException(
                    $"textDescription Unicode count {uniCount} requires {uniBytes} bytes but only {end - reader.Position} remain.");
            if (uniCount > 0)
            {
                ReadOnlySpan<byte> u = reader.ReadBytes((int)uniBytes);
                var charCount = (int)uniCount;
                while (charCount > 0)
                {
                    int idx = (charCount - 1) * 2;
                    if (u[idx] == 0 && u[idx + 1] == 0) charCount--;
                    else break;
                }
                unicode = Encoding.BigEndianUnicode.GetString(u.Slice(0, charCount * 2).ToArray());
            }
        }

        // ScriptCode + Macintosh section. The Macintosh block is always 67 bytes; an empty
        // tag still pads it out. Some profiles truncate, so be permissive.
        ushort scriptCode = 0;
        string? mac = null;
        if (end - reader.Position >= 2)
        {
            scriptCode = reader.ReadUInt16();
            if (end - reader.Position >= 1)
            {
                int macLen = reader.ReadUInt8();
                int macBlockLen = Math.Min(67, end - reader.Position);
                ReadOnlySpan<byte> macBlock = reader.ReadBytes(macBlockLen);
                if (macLen > 0 && macLen <= macBlock.Length)
                {
                    int len = macLen;
                    // Trim trailing NULs from the declared length.
                    while (len > 0 && macBlock[len - 1] == 0) len--;
                    mac = Encoding.ASCII.GetString(macBlock.Slice(0, len).ToArray());
                }
            }
        }

        // Advance reader to the end of the declared payload if we underran (some profiles
        // pad with extra bytes); on overrun ReadBytes would have already thrown.
        if (reader.Position < end) reader.Skip(end - reader.Position);

        return new TextDescriptionTagElement(ascii, langCode, unicode, scriptCode, mac);
    }

    private static string ReadAsciiNulTerminated(IccBinaryReader reader, int count)
    {
        if (count == 0) return string.Empty;
        ReadOnlySpan<byte> bytes = reader.ReadBytes(count);
        int end = bytes.Length;
        while (end > 0 && bytes[end - 1] == 0) end--;
        return Encoding.ASCII.GetString(bytes.Slice(0, end));
    }
}
