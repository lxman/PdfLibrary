using System.Collections.Generic;
using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Tags;

/// <summary>
/// ICC.1:2010 §10.4 colorantOrderType ('clro'). N uint8 indices giving the order in which
/// process colorants are laid down on the press (first index = first laid down).
/// </summary>
public sealed class ColorantOrderTagElement : TagElement
{
    public IReadOnlyList<byte> Order { get; }

    public ColorantOrderTagElement(IReadOnlyList<byte> order) : base(TagTypeSignatures.ColorantOrder)
    {
        Order = order;
    }

    internal static ColorantOrderTagElement Parse(IccBinaryReader reader, int payloadBytes)
    {
        if (payloadBytes < 4)
            throw new IccParseException($"colorantOrderType payload {payloadBytes} bytes; need at least 4.");
        uint count = reader.ReadUInt32();
        if (count > (uint)(payloadBytes - 4))
            throw new IccParseException(
                $"colorantOrderType declares {count} entries but only {payloadBytes - 4} bytes follow the count.");
        byte[] order = new byte[count];
        for (uint i = 0; i < count; i++) order[i] = reader.ReadUInt8();
        return new ColorantOrderTagElement(order);
    }
}
