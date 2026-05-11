using System;
using JpegCodec.Stream;

namespace JpegCodec.Segments;

// DRI (Define Restart Interval) segment, T.81 §B.2.4.4.
internal static class RestartInterval
{
    public static ushort Parse(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 2)
            throw new InvalidOperationException(
                $"DRI payload must be 2 bytes; was {payload.Length}.");
        return BigEndian.ReadUInt16(payload, 0);
    }
}
