using ICCSharp.Eval;
using ICCSharp.IO;
using ICCSharp.Profile;
using ICCSharp.Transform;

namespace ICCSharp.Tests.Transform;

public class PcsCodecTests
{
    private static readonly IccSignature XyzPcs = ColorSpaceSignatures.XYZ;
    private static readonly IccSignature LabPcs = ColorSpaceSignatures.Lab;

    // ---- XYZ PCS --------------------------------------------------------

    [Fact]
    public void Xyz_decode_scales_by_IccMaxXyz()
    {
        double[] raw = { 0.5, 0.5, 0.5 };
        double[] xyz = new double[3];
        PcsCodec.Decode(raw, xyz, XyzPcs);
        Assert.Equal(0.5 * IccConstants.IccMaxXyz, xyz[0], 12);
        Assert.Equal(0.5 * IccConstants.IccMaxXyz, xyz[1], 12);
        Assert.Equal(0.5 * IccConstants.IccMaxXyz, xyz[2], 12);
    }

    [Fact]
    public void Xyz_round_trip_recovers_input()
    {
        double[] original = { 0.3, 0.7, 0.42 };
        double[] absolute = new double[3];
        double[] backToRaw = new double[3];
        PcsCodec.Decode(original, absolute, XyzPcs);
        PcsCodec.Encode(absolute, backToRaw, XyzPcs);
        for (int i = 0; i < 3; i++)
            Assert.Equal(original[i], backToRaw[i], 12);
    }

    [Fact]
    public void Xyz_D50_white_corresponds_to_half_encoded()
    {
        // D50 white has Y = 1.0. Encoded = 1.0 / IccMaxXyz ≈ 0.5.
        double[] xyz = { StandardIlluminants.D50.X, StandardIlluminants.D50.Y, StandardIlluminants.D50.Z };
        double[] raw = new double[3];
        PcsCodec.Encode(xyz, raw, XyzPcs);
        Assert.Equal(0.5, raw[1], 4); // Y component
    }

    // ---- Lab PCS --------------------------------------------------------

    [Fact]
    public void Lab_decode_v4_white_to_unit_Y()
    {
        // Lab (100, 0, 0) is white. In v4 encoding: raw = (1.0, 0.5, 0.5).
        // After decode, expect XYZ ≈ D50 (with Y = 1.0).
        double[] raw = { 1.0, 0.5, 128.0 / 255.0 };
        // Better: Lab a=0 → (0+128)/255 = 0.502; b=0 likewise.
        raw[1] = 128.0 / 255.0;
        raw[2] = 128.0 / 255.0;

        double[] xyz = new double[3];
        PcsCodec.Decode(raw, xyz, LabPcs);
        Assert.Equal(StandardIlluminants.D50.X, xyz[0], 4);
        Assert.Equal(StandardIlluminants.D50.Y, xyz[1], 4);
        Assert.Equal(StandardIlluminants.D50.Z, xyz[2], 4);
    }

    [Fact]
    public void Lab_decode_black_to_zero()
    {
        double[] raw = { 0.0, 128.0 / 255.0, 128.0 / 255.0 };
        double[] xyz = new double[3];
        PcsCodec.Decode(raw, xyz, LabPcs);
        Assert.Equal(0.0, xyz[0], 6);
        Assert.Equal(0.0, xyz[1], 6);
        Assert.Equal(0.0, xyz[2], 6);
    }

    [Fact]
    public void Lab_round_trip_recovers_input()
    {
        // Use raw values that lie within the cube-root segment of the Lab function (i.e. above
        // the linear-toe threshold), since the inverse only round-trips perfectly there.
        double[] original = { 0.6, 130.0 / 255.0, 100.0 / 255.0 };
        double[] absolute = new double[3];
        double[] backToRaw = new double[3];
        PcsCodec.Decode(original, absolute, LabPcs);
        PcsCodec.Encode(absolute, backToRaw, LabPcs);
        for (int i = 0; i < 3; i++)
            Assert.Equal(original[i], backToRaw[i], 9);
    }

    [Fact]
    public void Lab_encode_white_xyz_to_normalized_unit_L()
    {
        // Absolute XYZ = D50 → Lab (100, 0, 0) → normalized (1.0, 0.502, 0.502).
        double[] xyz = { StandardIlluminants.D50.X, StandardIlluminants.D50.Y, StandardIlluminants.D50.Z };
        double[] raw = new double[3];
        PcsCodec.Encode(xyz, raw, LabPcs);
        Assert.Equal(1.0, raw[0], 4);
        Assert.Equal(128.0 / 255.0, raw[1], 3);
        Assert.Equal(128.0 / 255.0, raw[2], 3);
    }

    [Fact]
    public void IsLab_detects_Lab_signature()
    {
        Assert.True(PcsCodec.IsLab(LabPcs));
        Assert.False(PcsCodec.IsLab(XyzPcs));
    }
}
