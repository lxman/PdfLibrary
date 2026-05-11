using System;

namespace JpegCodec.Stream;

internal static class BigEndian
{
    public static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset)
    {
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    public static void WriteUInt16(Span<byte> data, int offset, ushort value)
    {
        data[offset] = (byte)(value >> 8);
        data[offset + 1] = (byte)value;
    }
}
