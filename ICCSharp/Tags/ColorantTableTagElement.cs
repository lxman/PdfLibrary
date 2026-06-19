using System.Collections.Generic;
using System.Text;
using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Tags;

/// <summary>
/// ICC.1:2010 §10.3 colorantTableType ('clrt'). One entry per colorant: 32-byte ASCII name +
/// three uint16 PCS values (encoded XYZ or Lab depending on header PCS).
/// </summary>
public sealed class ColorantTableTagElement : TagElement
{
    public readonly record struct Entry(string Name, ushort PCS1, ushort PCS2, ushort PCS3);

    public IReadOnlyList<Entry> Entries { get; }

    public ColorantTableTagElement(IReadOnlyList<Entry> entries) : base(TagTypeSignatures.ColorantTable)
    {
        Entries = entries;
    }

    internal static ColorantTableTagElement Parse(IccBinaryReader reader, int payloadBytes)
    {
        if (payloadBytes < 4)
            throw new IccParseException($"colorantTableType payload {payloadBytes} bytes; need at least 4.");
        uint count = reader.ReadUInt32();
        long needed = (long)count * 38;
        if (needed > payloadBytes - 4)
            throw new IccParseException(
                $"colorantTableType declares {count} entries ({needed} bytes); only {payloadBytes - 4} remain.");

        var entries = new Entry[count];
        for (uint i = 0; i < count; i++)
        {
            string name = TrimAsciiToNul(reader.ReadBytes(32));
            ushort p1 = reader.ReadUInt16();
            ushort p2 = reader.ReadUInt16();
            ushort p3 = reader.ReadUInt16();
            entries[i] = new Entry(name, p1, p2, p3);
        }
        return new ColorantTableTagElement(entries);
    }

    internal static string TrimAsciiToNul(System.ReadOnlySpan<byte> bytes)
    {
        int end = bytes.Length;
        for (var i = 0; i < bytes.Length; i++)
            if (bytes[i] == 0) { end = i; break; }
        return Encoding.ASCII.GetString(bytes.Slice(0, end).ToArray());
    }
}
