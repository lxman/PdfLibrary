using System;

namespace ICCSharp.IO;

/// <summary>
/// A four-byte ICC signature (ICC.1:2010 §4.4). Stored as a big-endian
/// packed uint so that comparisons stay cheap; surfaces a readable
/// four-character form for diagnostics.
/// </summary>
public readonly struct IccSignature : IEquatable<IccSignature>
{
    public uint Value { get; }

    public IccSignature(uint value) => Value = value;

    public static IccSignature FromAscii(string fourChars)
    {
        if (fourChars is null) throw new ArgumentNullException(nameof(fourChars));
        if (fourChars.Length != 4) throw new ArgumentException("Signature must be 4 characters.", nameof(fourChars));
        return new IccSignature(
            ((uint)(byte)fourChars[0] << 24) |
            ((uint)(byte)fourChars[1] << 16) |
            ((uint)(byte)fourChars[2] << 8)  |
             (byte)fourChars[3]);
    }

    public bool Equals(IccSignature other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is IccSignature s && Equals(s);
    public override int GetHashCode() => (int)Value;
    public static bool operator ==(IccSignature a, IccSignature b) => a.Value == b.Value;
    public static bool operator !=(IccSignature a, IccSignature b) => a.Value != b.Value;

    public override string ToString()
    {
        Span<char> chars = stackalloc char[4];
        chars[0] = (char)((Value >> 24) & 0xFF);
        chars[1] = (char)((Value >> 16) & 0xFF);
        chars[2] = (char)((Value >> 8) & 0xFF);
        chars[3] = (char)(Value & 0xFF);
        return new string(chars);
    }
}
