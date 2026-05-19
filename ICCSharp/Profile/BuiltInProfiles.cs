using System;

namespace ICCSharp.Profile;

/// <summary>
/// Factory for synthetic built-in ICC profiles. Useful when callers need a known destination
/// space (typically sRGB) without bundling profile bytes or depending on an OS-installed profile.
/// </summary>
public static class BuiltInProfiles
{
    private static IccProfile? _srgb;
    private static readonly object SrgbLock = new();

    /// <summary>
    /// IEC 61966-2-1 sRGB as a matrix/TRC profile: rXYZ/gXYZ/bXYZ colorants (D65 primaries
    /// chromatically adapted to D50 via Bradford) plus the parametric type-3 sRGB curve. Lazily
    /// constructed and cached.
    /// </summary>
    public static IccProfile Srgb
    {
        get
        {
            if (_srgb is not null) return _srgb;
            lock (SrgbLock)
            {
                if (_srgb is null)
                {
                    _srgb = IccProfile.Parse(BuildSrgbBytes());
                }
            }
            return _srgb;
        }
    }

    private static byte[] BuildSrgbBytes()
    {
        // sRGB primaries, Bradford-adapted to D50 (the canonical numbers from the ICC sRGB v4
        // reference profile; also the values lcms2 uses internally).
        (double X, double Y, double Z) red   = (0.43607, 0.22249, 0.01392);
        (double X, double Y, double Z) green = (0.38515, 0.71687, 0.09708);
        (double X, double Y, double Z) blue  = (0.14307, 0.06061, 0.71410);

        // Parametric type-3 sRGB curve coefficients.
        // y = (a·x + b)^g for x ≥ d; y = c·x for x < d
        const double g = 2.4;
        const double a = 1.0 / 1.055;
        const double b = 0.055 / 1.055;
        const double c = 1.0 / 12.92;
        const double d = 0.04045;

        // Tag layout:
        //   Header           128 bytes
        //   Tag count + table 4 + 6 × 12 = 76 bytes
        //   rXYZ tag         20 bytes (8 header + 12 body)
        //   gXYZ tag         20 bytes
        //   bXYZ tag         20 bytes
        //   rTRC tag         32 bytes (8 header + 4 fnType+reserved + 5×4 params)
        //   gTRC tag         32 bytes
        //   bTRC tag         32 bytes
        // All tags 4-byte aligned naturally.
        const int header = 128;
        const int tableStart = header + 4;
        const int dataStart = tableStart + 6 * 12;

        const int xyzSize = 20;
        const int trcSize = 32;

        int rXyzOff = dataStart;
        int gXyzOff = rXyzOff + xyzSize;
        int bXyzOff = gXyzOff + xyzSize;
        int rTrcOff = bXyzOff + xyzSize;
        int gTrcOff = rTrcOff + trcSize;
        int bTrcOff = gTrcOff + trcSize;
        int totalSize = bTrcOff + trcSize;

        byte[] data = new byte[totalSize];

        // Header — only the fields needed to make IccProfile.Parse happy.
        WriteUInt32(data, 0, (uint)totalSize);          // profile size
        WriteAscii(data, 12, "mntr");                   // Display class
        WriteAscii(data, 16, "RGB ");                   // RGB data color space
        WriteAscii(data, 20, "XYZ ");                   // XYZ PCS
        WriteAscii(data, 36, "acsp");                   // magic
        // Version field at offset 8: v4.3.0
        data[8] = 0x04; data[9] = 0x30; data[10] = 0; data[11] = 0;
        // D50 illuminant at offset 68
        WriteS15Fixed16(data, 68, 0.96422);
        WriteS15Fixed16(data, 72, 1.00000);
        WriteS15Fixed16(data, 76, 0.82521);

        // Tag table
        WriteUInt32(data, header, 6);
        WriteTagEntry(data, tableStart + 0 * 12, "rXYZ", (uint)rXyzOff, xyzSize);
        WriteTagEntry(data, tableStart + 1 * 12, "gXYZ", (uint)gXyzOff, xyzSize);
        WriteTagEntry(data, tableStart + 2 * 12, "bXYZ", (uint)bXyzOff, xyzSize);
        WriteTagEntry(data, tableStart + 3 * 12, "rTRC", (uint)rTrcOff, trcSize);
        WriteTagEntry(data, tableStart + 4 * 12, "gTRC", (uint)gTrcOff, trcSize);
        WriteTagEntry(data, tableStart + 5 * 12, "bTRC", (uint)bTrcOff, trcSize);

        WriteXyzTag(data, rXyzOff, red);
        WriteXyzTag(data, gXyzOff, green);
        WriteXyzTag(data, bXyzOff, blue);
        WriteParaTag(data, rTrcOff, g, a, b, c, d);
        WriteParaTag(data, gTrcOff, g, a, b, c, d);
        WriteParaTag(data, bTrcOff, g, a, b, c, d);

        return data;
    }

    private static void WriteTagEntry(byte[] buf, int offset, string sig, uint dataOffset, int size)
    {
        for (int i = 0; i < 4; i++) buf[offset + i] = (byte)sig[i];
        WriteUInt32(buf, offset + 4, dataOffset);
        WriteUInt32(buf, offset + 8, (uint)size);
    }

    private static void WriteXyzTag(byte[] buf, int offset, (double X, double Y, double Z) xyz)
    {
        WriteAscii(buf, offset, "XYZ ");
        WriteS15Fixed16(buf, offset + 8, xyz.X);
        WriteS15Fixed16(buf, offset + 12, xyz.Y);
        WriteS15Fixed16(buf, offset + 16, xyz.Z);
    }

    private static void WriteParaTag(byte[] buf, int offset, double g, double a, double b, double c, double d)
    {
        WriteAscii(buf, offset, "para");
        // bytes 4-7 reserved already zero
        WriteUInt16(buf, offset + 8, 3);   // function type 3
        // bytes 10-11 reserved
        WriteS15Fixed16(buf, offset + 12, g);
        WriteS15Fixed16(buf, offset + 16, a);
        WriteS15Fixed16(buf, offset + 20, b);
        WriteS15Fixed16(buf, offset + 24, c);
        WriteS15Fixed16(buf, offset + 28, d);
    }

    private static void WriteUInt16(byte[] buf, int offset, ushort value)
    {
        buf[offset]     = (byte)((value >> 8) & 0xFF);
        buf[offset + 1] = (byte)(value & 0xFF);
    }

    private static void WriteUInt32(byte[] buf, int offset, uint value)
    {
        buf[offset]     = (byte)((value >> 24) & 0xFF);
        buf[offset + 1] = (byte)((value >> 16) & 0xFF);
        buf[offset + 2] = (byte)((value >> 8) & 0xFF);
        buf[offset + 3] = (byte)(value & 0xFF);
    }

    private static void WriteAscii(byte[] buf, int offset, string s)
    {
        for (int i = 0; i < s.Length; i++) buf[offset + i] = (byte)s[i];
    }

    private static void WriteS15Fixed16(byte[] buf, int offset, double value)
    {
        int raw = (int)Math.Round(value * 65536.0);
        buf[offset]     = (byte)((raw >> 24) & 0xFF);
        buf[offset + 1] = (byte)((raw >> 16) & 0xFF);
        buf[offset + 2] = (byte)((raw >> 8) & 0xFF);
        buf[offset + 3] = (byte)(raw & 0xFF);
    }
}
