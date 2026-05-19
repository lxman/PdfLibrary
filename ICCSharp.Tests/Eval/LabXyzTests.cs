using ICCSharp.Eval;
using ICCSharp.IO;

namespace ICCSharp.Tests.Eval;

public class LabXyzTests
{
    [Fact]
    public void Reference_white_maps_to_L100_a0_b0()
    {
        LabNumber lab = LabXyzConverter.ToLab(StandardIlluminants.D50, StandardIlluminants.D50);
        Assert.Equal(100.0, lab.L, 6);
        Assert.Equal(0.0, lab.A, 6);
        Assert.Equal(0.0, lab.B, 6);
    }

    [Fact]
    public void Pure_black_maps_to_L0()
    {
        LabNumber lab = LabXyzConverter.ToLab(new XyzNumber(0, 0, 0), StandardIlluminants.D50);
        Assert.Equal(0.0, lab.L, 6);
        Assert.Equal(0.0, lab.A, 6);
        Assert.Equal(0.0, lab.B, 6);
    }

    [Fact]
    public void XYZ_to_Lab_to_XYZ_round_trips()
    {
        foreach (XyzNumber xyz in new[]
        {
            new XyzNumber(0.5, 0.5, 0.5),
            new XyzNumber(0.1, 0.2, 0.3),
            new XyzNumber(0.9, 0.8, 0.7),
            new XyzNumber(0.0, 0.0, 0.0),
            new XyzNumber(0.96422, 1.0, 0.82521),
        })
        {
            LabNumber lab = LabXyzConverter.ToLab(xyz, StandardIlluminants.D50);
            XyzNumber back = LabXyzConverter.ToXyz(lab, StandardIlluminants.D50);
            Assert.Equal(xyz.X, back.X, 10);
            Assert.Equal(xyz.Y, back.Y, 10);
            Assert.Equal(xyz.Z, back.Z, 10);
        }
    }

    [Fact]
    public void Lab_to_XYZ_to_Lab_round_trips()
    {
        foreach (LabNumber lab in new[]
        {
            new LabNumber(50, 0, 0),
            new LabNumber(75, 25, -10),
            new LabNumber(25, -40, 60),
            new LabNumber(100, 0, 0),
            new LabNumber(0, 0, 0),
        })
        {
            XyzNumber xyz = LabXyzConverter.ToXyz(lab, StandardIlluminants.D50);
            LabNumber back = LabXyzConverter.ToLab(xyz, StandardIlluminants.D50);
            Assert.Equal(lab.L, back.L, 9);
            Assert.Equal(lab.A, back.A, 9);
            Assert.Equal(lab.B, back.B, 9);
        }
    }

    [Fact]
    public void Linear_segment_below_threshold_uses_piecewise_form()
    {
        // For Y/Yn < (6/29)³ ≈ 0.008856 the piecewise function uses the linear segment, not the
        // cube root. f(0) = 4/29; L = 116·(4/29) − 16 = 0.
        LabNumber lab = LabXyzConverter.ToLab(new XyzNumber(0, 0, 0), StandardIlluminants.D50);
        Assert.Equal(0.0, lab.L, 12);

        // Just above threshold: Y/Yn = 0.01, Y = 0.01·1.0 = 0.01
        LabNumber lab2 = LabXyzConverter.ToLab(new XyzNumber(0.01, 0.01, 0.01), StandardIlluminants.D50);
        // L = 116·(0.01^(1/3)) − 16 ≈ 116·0.2154 − 16 ≈ 8.99
        Assert.InRange(lab2.L, 8.5, 9.5);
    }
}
