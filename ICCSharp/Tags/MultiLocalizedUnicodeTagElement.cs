using System;
using System.Collections.Generic;
using System.Text;
using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Tags;

/// <summary>
/// ICC.1:2010 §10.13 multiLocalizedUnicodeType ('mluc'). Carries N (languageCode, countryCode, UTF-16BE text)
/// records. Offsets in the record table are measured from the start of the tag data (i.e. from the type
/// signature, not from the end of the type header).
/// </summary>
public sealed class MultiLocalizedUnicodeTagElement : TagElement
{
    public IReadOnlyList<LocalizedString> Records { get; }

    public MultiLocalizedUnicodeTagElement(IReadOnlyList<LocalizedString> records)
        : base(TagTypeSignatures.MultiLocalizedUnicode)
    {
        Records = records;
    }

    /// <summary>Returns the first record's text, or empty if none. Useful when callers just want "the" description.</summary>
    public string FirstText => Records.Count == 0 ? string.Empty : Records[0].Text;

    /// <summary>
    /// Parses an 'mluc' tag. <paramref name="fullTagData"/> is the ENTIRE tag slice (including the 8-byte
    /// type header), because record offsets are measured from the start of the tag.
    /// </summary>
    internal static MultiLocalizedUnicodeTagElement Parse(IccBinaryReader reader, int payloadBytes, ReadOnlyMemory<byte> fullTagData)
    {
        if (payloadBytes < 8)
            throw new IccParseException($"mluc payload {payloadBytes} bytes; need at least 8 for record count + size.");

        uint recordCount = reader.ReadUInt32();
        uint recordSize = reader.ReadUInt32();
        if (recordSize < 12)
            throw new IccParseException($"mluc record size {recordSize} below required 12 bytes.");

        long tableBytes = (long)recordCount * recordSize;
        if (tableBytes > payloadBytes - 8)
            throw new IccParseException(
                $"mluc record table needs {tableBytes} bytes but only {payloadBytes - 8} remain in payload.");

        var records = new LocalizedString[recordCount];
        ReadOnlySpan<byte> fullSpan = fullTagData.Span;
        int tagLength = fullTagData.Length;

        for (uint i = 0; i < recordCount; i++)
        {
            ushort lang = reader.ReadUInt16();
            ushort country = reader.ReadUInt16();
            uint length = reader.ReadUInt32();
            uint offset = reader.ReadUInt32();

            long end = (long)offset + length;
            if (offset < 8 || end > tagLength)
                throw new IccParseException(
                    $"mluc record {i} string range [{offset}, {end}) outside tag bounds [8, {tagLength}).");
            if ((length & 1) != 0)
                throw new IccParseException($"mluc record {i} length {length} not a multiple of 2 (UTF-16).");

            ReadOnlySpan<byte> strBytes = fullSpan.Slice((int)offset, (int)length);
            var charCount = (int)(length / 2);
            // Some encoders include trailing NUL; trim defensively.
            while (charCount > 0)
            {
                int idx = (charCount - 1) * 2;
                if (strBytes[idx] == 0 && strBytes[idx + 1] == 0) charCount--;
                else break;
            }
            string text = Encoding.BigEndianUnicode.GetString(strBytes.Slice(0, charCount * 2).ToArray());

            // Skip remaining bytes of record beyond the 12-byte minimum (spec allows recordSize > 12).
            if (recordSize > 12) reader.Skip((int)recordSize - 12);

            records[i] = new LocalizedString(LangCodeToString(lang), LangCodeToString(country), text);
        }

        return new MultiLocalizedUnicodeTagElement(records);
    }

    private static string LangCodeToString(ushort code)
    {
        if (code == 0) return string.Empty;
        var a = (char)((code >> 8) & 0xFF);
        var b = (char)(code & 0xFF);
        return new string(new[] { a, b });
    }
}
