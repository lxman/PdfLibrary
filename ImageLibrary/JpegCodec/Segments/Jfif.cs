using System;

namespace JpegCodec.Segments;

// APP0 JFIF segment. ISO/IEC 10918-5 (T.871). Layout:
//   "JFIF\0" (5 bytes)
//   1 byte  Major version
//   1 byte  Minor version
//   1 byte  Density units (0/1/2)
//   2 bytes Xdensity
//   2 bytes Ydensity
//   1 byte  Xthumbnail
//   1 byte  Ythumbnail
//   ...     thumbnail data
internal static class Jfif
{
    public static bool IsJfif(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 5) return false;
        return payload[0] == (byte)'J' && payload[1] == (byte)'F' &&
               payload[2] == (byte)'I' && payload[3] == (byte)'F' &&
               payload[4] == 0x00;
    }
}
