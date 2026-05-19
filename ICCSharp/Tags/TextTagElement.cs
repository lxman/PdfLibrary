using System;
using System.Text;
using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Tags;

/// <summary>
/// ICC.1:2010 §10.21 textType — a single 7-bit ASCII string, null-terminated.
/// Trailing NUL is stripped; embedded NULs are preserved (spec ambiguity, but lcms2
/// behaves this way).
/// </summary>
public sealed class TextTagElement : TagElement
{
    public string Value { get; }

    public TextTagElement(string value) : base(TagTypeSignatures.Text)
    {
        Value = value;
    }

    internal static TextTagElement Parse(IccBinaryReader reader, int payloadBytes)
    {
        if (payloadBytes < 0)
            throw new IccParseException($"textType payload length {payloadBytes} invalid.");
        ReadOnlySpan<byte> bytes = reader.ReadBytes(payloadBytes);
        int end = bytes.Length;
        while (end > 0 && bytes[end - 1] == 0) end--;
        string value = Encoding.ASCII.GetString(bytes.Slice(0, end));
        return new TextTagElement(value);
    }
}
