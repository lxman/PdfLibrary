using ICCSharp.Profile;

namespace ICCSharp.Tests.Transform;

/// <summary>
/// Covers monochrome (1-channel, GRAY + kTRC) source profiles transforming to sRGB. A gray
/// profile carries neither an A2B LUT nor the 6-tag RGB matrix/TRC family, so it exercises the
/// dedicated gray-TRC pipeline rather than <see cref="ICCSharp.Transform.MatrixTrcToPcs"/>.
///
/// The neutral axis is anchored to the D50 PCS white (relative-colorimetric convention), so gray
/// inputs must produce perfectly neutral sRGB outputs regardless of the profile's stored wtpt.
/// </summary>
public class GrayProfileTests
{
    // Gray Gamma 2.2 working-space profile shipped with Windows/Adobe; skipped if not installed.
    private static readonly string GrayPath =
        @"C:\Windows\System32\spool\drivers\color\ewgray22.icm";

    // --- Synthetic minimal gray profile ---------------------------------

    [Fact]
    public void Synthetic_gray_profile_builds_a_one_to_three_channel_transform()
    {
        IccProfile gray = IccProfile.Parse(BuildGrayProfile(2.2));
        Assert.Equal(ColorSpaceSignatures.Gray, gray.Header.DataColorSpace);

        var t = IccTransform.Create(gray, BuiltInProfiles.Srgb);
        Assert.Equal(1, t.InputChannels);
        Assert.Equal(3, t.OutputChannels);
    }

    [Fact]
    public void Synthetic_gray_white_and_black_map_to_neutral_srgb_endpoints()
    {
        var t = IccTransform.Create(IccProfile.Parse(BuildGrayProfile(2.2)), BuiltInProfiles.Srgb);

        double[] white = t.Apply(1.0);
        double[] black = t.Apply(0.0);

        Assert.True(white[0] > 0.99 && white[1] > 0.99 && white[2] > 0.99,
            $"gray white should map to sRGB white; got ({white[0]:F4}, {white[1]:F4}, {white[2]:F4})");
        Assert.True(black[0] < 0.01 && black[1] < 0.01 && black[2] < 0.01,
            $"gray black should map to sRGB black; got ({black[0]:F4}, {black[1]:F4}, {black[2]:F4})");

        // Neutral: equal channels.
        Assert.Equal(white[0], white[1], 3);
        Assert.Equal(white[1], white[2], 3);
    }

    [Fact]
    public void Synthetic_gray_midpoint_is_neutral_and_near_half()
    {
        var t = IccTransform.Create(IccProfile.Parse(BuildGrayProfile(2.2)), BuiltInProfiles.Srgb);

        double[] mid = t.Apply(0.5);

        // Gamma-2.2 gray is very close to sRGB, so device 0.5 lands near sRGB 0.5.
        Assert.InRange(mid[0], 0.47, 0.53);
        Assert.Equal(mid[0], mid[1], 3);
        Assert.Equal(mid[1], mid[2], 3);
    }

    [Fact]
    public void Synthetic_gray_ramp_is_monotonic()
    {
        var t = IccTransform.Create(IccProfile.Parse(BuildGrayProfile(2.2)), BuiltInProfiles.Srgb);

        double prev = -1.0;
        for (var i = 0; i <= 10; i++)
        {
            double r = t.Apply(i / 10.0)[0];
            Assert.True(r >= prev - 1e-9, $"ramp not monotonic at step {i}: {r:F4} < {prev:F4}");
            prev = r;
        }
    }

    // --- Real OS gray profile (skip silently when absent) ---------------

    [Fact]
    public void Real_gray_gamma_profile_transforms_to_srgb()
    {
        if (!File.Exists(GrayPath)) return;

        IccProfile gray = IccProfile.Parse(File.ReadAllBytes(GrayPath));
        Assert.Equal(ColorSpaceSignatures.Gray, gray.Header.DataColorSpace);

        var t = IccTransform.Create(gray, BuiltInProfiles.Srgb);
        Assert.Equal(1, t.InputChannels);
        Assert.Equal(3, t.OutputChannels);

        double[] white = t.Apply(1.0);
        double[] black = t.Apply(0.0);
        Assert.True(white[0] > 0.98, $"white R = {white[0]:F4}");
        Assert.True(black[0] < 0.02, $"black R = {black[0]:F4}");

        // Output must be neutral even though this profile's wtpt is D65, not D50.
        Assert.Equal(white[0], white[2], 2);
        Assert.Equal(black[0], black[2], 2);

        double prev = -1.0;
        for (var i = 0; i <= 10; i++)
        {
            double v = t.Apply(i / 10.0)[0];
            Assert.True(v >= prev - 1e-9, $"non-monotonic ramp at step {i}");
            prev = v;
        }
    }

    // --- Byte assembly (mirrors BuiltInProfiles' writer style) ----------

    /// <summary>
    /// Builds a minimal monochrome ICC profile: GRAY data space, XYZ PCS, a single pure-gamma kTRC
    /// curve, and a D50 wtpt.
    /// </summary>
    private static byte[] BuildGrayProfile(double gamma)
    {
        const int header = 128;
        const int tableStart = header + 4;
        const int dataStart = tableStart + 2 * 12;   // 2 tag-table entries

        const int xyzSize = 20;    // 8-byte type header + 12-byte body
        const int curvSize = 16;   // 8-byte type header + 4-byte count + 2-byte sample (+2 pad)

        int kTrcOff = dataStart + xyzSize;
        int totalSize = kTrcOff + curvSize;

        var d = new byte[totalSize];

        WriteUInt32(d, 0, (uint)totalSize);
        WriteAscii(d, 12, "mntr");      // display class
        WriteAscii(d, 16, "GRAY");      // 1-channel data space
        WriteAscii(d, 20, "XYZ ");      // PCS
        WriteAscii(d, 36, "acsp");      // magic
        d[8] = 0x04; d[9] = 0x30;       // version 4.3
        WriteS15Fixed16(d, 68, 0.96422);
        WriteS15Fixed16(d, 72, 1.00000);
        WriteS15Fixed16(d, 76, 0.82521);

        WriteUInt32(d, header, 2);      // tag count
        WriteTagEntry(d, tableStart + 0 * 12, "wtpt", (uint)dataStart, xyzSize);
        WriteTagEntry(d, tableStart + 1 * 12, "kTRC", (uint)kTrcOff, curvSize);

        // wtpt = D50
        WriteAscii(d, dataStart, "XYZ ");
        WriteS15Fixed16(d, dataStart + 8, 0.96422);
        WriteS15Fixed16(d, dataStart + 12, 1.00000);
        WriteS15Fixed16(d, dataStart + 16, 0.82521);

        // kTRC = single-gamma curv (count == 1, u8Fixed8 gamma sample)
        WriteAscii(d, kTrcOff, "curv");
        WriteUInt32(d, kTrcOff + 8, 1);
        WriteUInt16(d, kTrcOff + 12, (ushort)Math.Round(gamma * 256.0));

        return d;
    }

    private static void WriteTagEntry(byte[] buf, int offset, string sig, uint dataOffset, int size)
    {
        for (var i = 0; i < 4; i++) buf[offset + i] = (byte)sig[i];
        WriteUInt32(buf, offset + 4, dataOffset);
        WriteUInt32(buf, offset + 8, (uint)size);
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
        for (var i = 0; i < s.Length; i++) buf[offset + i] = (byte)s[i];
    }

    private static void WriteS15Fixed16(byte[] buf, int offset, double value)
    {
        var raw = (int)Math.Round(value * 65536.0);
        buf[offset]     = (byte)((raw >> 24) & 0xFF);
        buf[offset + 1] = (byte)((raw >> 16) & 0xFF);
        buf[offset + 2] = (byte)((raw >> 8) & 0xFF);
        buf[offset + 3] = (byte)(raw & 0xFF);
    }
}
