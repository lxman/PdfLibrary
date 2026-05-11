using System;

namespace JpegCodec.Segments;

// APP14 Adobe segment. Not in T.81 — Adobe extension documented in the
// "Adobe Photoshop, TIFF Technical Notes" and replicated in libjpeg's
// jpeg-mark.c. Layout:
//   "Adobe\0" (5 bytes)   ← ASCII identifier
//   2 bytes               DCTEncodeVersion (typically 0x0064 = 100)
//   2 bytes               APP14Flags0
//   2 bytes               APP14Flags1
//   1 byte                ColorTransform (0=Unknown/CMYK, 1=YCbCr, 2=YCCK)
internal sealed class AdobeApp14
{
    public byte ColorTransform { get; }

    private AdobeApp14(byte colorTransform)
    {
        ColorTransform = colorTransform;
    }

    public static bool TryParse(ReadOnlySpan<byte> payload, out AdobeApp14? marker)
    {
        marker = null;
        if (payload.Length < 12) return false;
        if (payload[0] != (byte)'A' || payload[1] != (byte)'d' ||
            payload[2] != (byte)'o' || payload[3] != (byte)'b' ||
            payload[4] != (byte)'e')
            return false;

        byte colorTransform = payload[11];
        marker = new AdobeApp14(colorTransform);
        return true;
    }
}
