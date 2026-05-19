using System.Collections.Generic;
using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Tags;

/// <summary>
/// ICC.1:2010 §10.27 XYZType — one or more XYZNumber values.
/// Used by wtpt, bkpt, rXYZ, gXYZ, bXYZ, lumi, and others (single XYZ in those cases).
/// </summary>
public sealed class XyzTagElement : TagElement
{
    public IReadOnlyList<XyzNumber> Values { get; }

    public XyzTagElement(IReadOnlyList<XyzNumber> values)
        : base(TagTypeSignatures.Xyz)
    {
        Values = values;
    }

    /// <summary>Reads the payload of an 'XYZ ' tag. Cursor must be past the 8-byte type header.</summary>
    internal static XyzTagElement Parse(IccBinaryReader reader, int payloadBytes)
    {
        if (payloadBytes < 0 || payloadBytes % 12 != 0)
            throw new IccParseException($"XYZType payload length {payloadBytes} is not a multiple of 12.");
        int count = payloadBytes / 12;
        XyzNumber[] values = new XyzNumber[count];
        for (int i = 0; i < count; i++) values[i] = reader.ReadXyz();
        return new XyzTagElement(values);
    }
}
