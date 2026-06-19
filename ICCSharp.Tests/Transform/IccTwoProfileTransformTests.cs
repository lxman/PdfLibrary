using ICCSharp.Eval;
using ICCSharp.IO;
using ICCSharp.Profile;
using ICCSharp.Transform;

namespace ICCSharp.Tests.Transform;

public class IccTwoProfileTransformTests
{
    private static readonly string SrgbPath =
        @"C:\Windows\System32\spool\drivers\color\sRGB Color Space Profile.icm";

    [Fact]
    public void SRGB_to_SRGB_is_near_identity()
    {
        if (!File.Exists(SrgbPath)) return;
        IccProfile srgb = IccProfile.Parse(File.ReadAllBytes(SrgbPath));

        IccTwoProfileTransform t = new(srgb, srgb);
        Assert.Equal(3, t.InputChannels);
        Assert.Equal(3, t.OutputChannels);

        foreach ((double r, double g, double b) in new[]
        {
            (0.0, 0.0, 0.0),
            (1.0, 1.0, 1.0),
            (0.5, 0.5, 0.5),
            (0.1, 0.5, 0.9),
            (0.8, 0.2, 0.6),
        })
        {
            double[] output = t.Apply(new[] { r, g, b });
            Assert.Equal(r, output[0], 4);
            Assert.Equal(g, output[1], 4);
            Assert.Equal(b, output[2], 4);
        }
    }

    [Fact]
    public void SRGB_to_SRGB_with_BPC_is_still_near_identity()
    {
        if (!File.Exists(SrgbPath)) return;
        IccProfile srgb = IccProfile.Parse(File.ReadAllBytes(SrgbPath));

        IccTwoProfileTransform t = new(srgb, srgb, blackPointCompensation: true);
        double[] output = t.Apply(new[] { 0.5, 0.5, 0.5 });
        Assert.Equal(0.5, output[0], 3);
        Assert.Equal(0.5, output[1], 3);
        Assert.Equal(0.5, output[2], 3);
    }

    [Fact]
    public void Matrix_TRC_round_trip_through_PCS_is_lossless_within_quantization()
    {
        // Synthetic matrix/TRC profile pair: identical profiles → round trip exactly recovers input.
        IccProfile profile = BuildSyntheticSrgbMatrixTrc();
        IccTwoProfileTransform t = new(profile, profile);

        foreach ((double r, double g, double b) in new[]
        {
            (0.2, 0.4, 0.6),
            (0.7, 0.3, 0.1),
            (1.0, 1.0, 1.0),
            (0.0, 0.0, 0.0),
        })
        {
            double[] output = t.Apply(new[] { r, g, b });
            Assert.Equal(r, output[0], 8);
            Assert.Equal(g, output[1], 8);
            Assert.Equal(b, output[2], 8);
        }
    }

    [Fact]
    public void Different_matrix_profiles_change_the_output()
    {
        // sRGB → AdobeRGB-like (wider gamut) should not be identity for saturated colors.
        IccProfile srgb = BuildSyntheticSrgbMatrixTrc();
        IccProfile wide = BuildSyntheticWideMatrixTrc();
        IccTwoProfileTransform t = new(srgb, wide);

        // Pure red in sRGB does not equal pure red in AdobeRGB (the latter has a wider gamut).
        double[] outRed = t.Apply(new[] { 1.0, 0.0, 0.0 });
        Assert.NotEqual(1.0, outRed[0], 3);
    }

    [Fact]
    public void Mismatched_input_count_throws()
    {
        if (!File.Exists(SrgbPath)) return;
        IccProfile srgb = IccProfile.Parse(File.ReadAllBytes(SrgbPath));
        IccTwoProfileTransform t = new(srgb, srgb);
        Assert.Throws<ArgumentException>(() => t.Apply(new[] { 0.5, 0.5 }));
    }

    // -- Synthetic profile builders -------------------------------------

    private static IccProfile BuildSyntheticSrgbMatrixTrc()
        => BuildMatrixTrcProfile(
            redXyz:   new XyzNumber(0.4360, 0.2225, 0.0139),  // sRGB primaries adapted to D50
            greenXyz: new XyzNumber(0.3851, 0.7169, 0.0971),
            blueXyz:  new XyzNumber(0.1431, 0.0606, 0.7141),
            gamma: 2.2);

    private static IccProfile BuildSyntheticWideMatrixTrc()
        => BuildMatrixTrcProfile(
            redXyz:   new XyzNumber(0.6097, 0.3111, 0.0195),  // wider-than-sRGB primaries
            greenXyz: new XyzNumber(0.2053, 0.6257, 0.0609),
            blueXyz:  new XyzNumber(0.1492, 0.0632, 0.7445),
            gamma: 2.2);

    private static IccProfile BuildMatrixTrcProfile(
        XyzNumber redXyz, XyzNumber greenXyz, XyzNumber blueXyz, double gamma)
    {
        // Header (128) + tagCount(4) + 6 entries × 12 + tag data.
        // 6 tags: rXYZ, gXYZ, bXYZ, rTRC, gTRC, bTRC.
        // Each XYZ tag: 8 type header + 12 body = 20 bytes.
        // Each TRC tag: 8 + 4 (count=0 → identity? no, we want gamma) + 2 (1 sample) = 14 bytes.
        // For a 'curv' with single u8Fixed8 sample = gamma: 12 + 2 = 14 bytes.
        // Pad each tag to 4-byte boundary.

        const int header = 128;
        int tableStart = header + 4;
        int tableSize = 6 * 12;
        int dataStart = tableStart + tableSize;

        // XYZ tag size = 20, padded = 20.
        // curv tag with 1 sample = 14, padded to 16.
        const int xyzSize = 20;
        const int trcSize = 14;
        const int trcPad = (trcSize + 3) & ~3; // 16

        int gXyzOff = dataStart + xyzSize;
        int bXyzOff = gXyzOff + xyzSize;
        int rTrcOff = bXyzOff + xyzSize;
        int gTrcOff = rTrcOff + trcPad;
        int bTrcOff = gTrcOff + trcPad;
        int totalSize = bTrcOff + trcPad;

        var data = new byte[totalSize];
        WriteUInt32(data, 0, (uint)totalSize);
        WriteAscii(data, 12, "mntr"); // Display class
        WriteAscii(data, 16, "RGB ");
        WriteAscii(data, 20, "XYZ ");
        WriteAscii(data, 36, "acsp");

        // Tag table
        WriteUInt32(data, header, 6);
        WriteTagEntry(data, tableStart + 0 * 12, "rXYZ", (uint)dataStart, xyzSize);
        WriteTagEntry(data, tableStart + 1 * 12, "gXYZ", (uint)gXyzOff, xyzSize);
        WriteTagEntry(data, tableStart + 2 * 12, "bXYZ", (uint)bXyzOff, xyzSize);
        WriteTagEntry(data, tableStart + 3 * 12, "rTRC", (uint)rTrcOff, trcSize);
        WriteTagEntry(data, tableStart + 4 * 12, "gTRC", (uint)gTrcOff, trcSize);
        WriteTagEntry(data, tableStart + 5 * 12, "bTRC", (uint)bTrcOff, trcSize);

        WriteXyzTag(data, dataStart, redXyz);
        WriteXyzTag(data, gXyzOff, greenXyz);
        WriteXyzTag(data, bXyzOff, blueXyz);
        WriteCurvTagGamma(data, rTrcOff, gamma);
        WriteCurvTagGamma(data, gTrcOff, gamma);
        WriteCurvTagGamma(data, bTrcOff, gamma);

        return IccProfile.Parse(data);
    }

    private static void WriteTagEntry(byte[] buf, int offset, string sig, uint dataOffset, int size)
    {
        for (var i = 0; i < 4; i++) buf[offset + i] = (byte)sig[i];
        WriteUInt32(buf, offset + 4, dataOffset);
        WriteUInt32(buf, offset + 8, (uint)size);
    }

    private static void WriteXyzTag(byte[] buf, int offset, XyzNumber xyz)
    {
        WriteAscii(buf, offset, "XYZ ");
        WriteS15Fixed16(buf, offset + 8, xyz.X);
        WriteS15Fixed16(buf, offset + 12, xyz.Y);
        WriteS15Fixed16(buf, offset + 16, xyz.Z);
    }

    private static void WriteCurvTagGamma(byte[] buf, int offset, double gamma)
    {
        WriteAscii(buf, offset, "curv");
        WriteUInt32(buf, offset + 8, 1); // count = 1
        var g = (ushort)Math.Round(gamma * 256.0);
        buf[offset + 12] = (byte)((g >> 8) & 0xFF);
        buf[offset + 13] = (byte)(g & 0xFF);
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
