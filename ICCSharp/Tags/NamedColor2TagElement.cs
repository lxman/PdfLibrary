using System.Collections.Generic;
using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Tags;

/// <summary>
/// ICC.1:2010 §10.15 namedColor2Type ('ncl2'). A vendor flag, a prefix/suffix (commonly used to
/// brand the palette, e.g. "PANTONE"), and N named entries each with PCS coordinates and optional
/// device coordinates.
/// </summary>
public sealed class NamedColor2TagElement : TagElement
{
    public readonly record struct Entry(string Name, ushort[] PcsCoords, ushort[] DeviceCoords);

    public uint VendorFlag { get; }
    public uint DeviceCoordCount { get; }
    public string Prefix { get; }
    public string Suffix { get; }
    public IReadOnlyList<Entry> Entries { get; }

    public NamedColor2TagElement(
        uint vendorFlag, uint deviceCoordCount, string prefix, string suffix, IReadOnlyList<Entry> entries)
        : base(TagTypeSignatures.NamedColor2)
    {
        VendorFlag = vendorFlag;
        DeviceCoordCount = deviceCoordCount;
        Prefix = prefix;
        Suffix = suffix;
        Entries = entries;
    }

    internal static NamedColor2TagElement Parse(IccBinaryReader reader, int payloadBytes)
    {
        // Fixed prefix: vendor(4) + count(4) + n(4) + prefix(32) + suffix(32) = 76 bytes.
        if (payloadBytes < 76)
            throw new IccParseException($"namedColor2Type payload {payloadBytes} bytes; need at least 76.");
        uint vendor = reader.ReadUInt32();
        uint count = reader.ReadUInt32();
        uint n = reader.ReadUInt32();
        string prefix = ColorantTableTagElement.TrimAsciiToNul(reader.ReadBytes(32));
        string suffix = ColorantTableTagElement.TrimAsciiToNul(reader.ReadBytes(32));

        int entrySize = 32 + 3 * 2 + (int)n * 2;
        long entriesBytes = (long)count * entrySize;
        if (entriesBytes > payloadBytes - 76)
            throw new IccParseException(
                $"namedColor2Type declares {count} entries × {entrySize} bytes ({entriesBytes}); only {payloadBytes - 76} remain.");

        Entry[] entries = new Entry[count];
        for (uint i = 0; i < count; i++)
        {
            string name = ColorantTableTagElement.TrimAsciiToNul(reader.ReadBytes(32));
            ushort[] pcs = new ushort[3];
            for (int j = 0; j < 3; j++) pcs[j] = reader.ReadUInt16();
            ushort[] dev = new ushort[n];
            for (int j = 0; j < n; j++) dev[j] = reader.ReadUInt16();
            entries[i] = new Entry(name, pcs, dev);
        }
        return new NamedColor2TagElement(vendor, n, prefix, suffix, entries);
    }
}
