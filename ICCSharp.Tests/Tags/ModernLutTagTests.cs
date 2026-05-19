using ICCSharp.Profile;
using ICCSharp.Tags;

namespace ICCSharp.Tests.Tags;

public class ModernLutTagTests
{
    private static byte[] U32Be(uint v) => new[]
    {
        (byte)((v >> 24) & 0xFF), (byte)((v >> 16) & 0xFF),
        (byte)((v >> 8) & 0xFF),  (byte)(v & 0xFF),
    };

    private static byte[] U16Be(ushort v) => new[] { (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF) };

    /// <summary>Emits a minimal identity 'curv' element (count=0), 12 bytes total.</summary>
    private static byte[] IdentityCurv()
        => [(byte)'c', (byte)'u', (byte)'r', (byte)'v', 0, 0, 0, 0, ..U32Be(0)];

    // --- mAB minimal: only B curves --------------------------------------

    [Fact]
    public void MAB_with_only_required_B_curves()
    {
        // Layout:
        //   0..7    'mAB '+reserved
        //   8..9    i=3, o=3
        //   10..11  reserved
        //   12..15  offBCurves   = 32
        //   16..19  offMatrix    = 0 (absent)
        //   20..23  offMCurves   = 0
        //   24..27  offClut      = 0
        //   28..31  offACurves   = 0
        //   32..    B curves (3 identity curves, 12 bytes each = 36 bytes)
        byte[] data = new byte[32 + 36];
        for (int i = 0; i < 4; i++) data[i] = (byte)"mAB "[i];
        data[8] = 3; data[9] = 3;
        Array.Copy(U32Be(32), 0, data, 12, 4);
        // others left zero

        byte[] curve = IdentityCurv();
        for (int c = 0; c < 3; c++)
            Buffer.BlockCopy(curve, 0, data, 32 + 12 * c, 12);

        LutAToBTagElement t = Assert.IsType<LutAToBTagElement>(TagElementReader.Parse(data));
        Assert.Equal(3, t.InputChannels);
        Assert.Equal(3, t.OutputChannels);
        Assert.Null(t.ACurves);
        Assert.Null(t.Clut);
        Assert.Null(t.MCurves);
        Assert.Null(t.Matrix);
        Assert.Equal(3, t.BCurves.Count);
        Assert.IsType<CurveTagElement>(t.BCurves[0]);
        Assert.True(((CurveTagElement)t.BCurves[0]).IsIdentity);
    }

    [Fact]
    public void MAB_missing_B_curves_offset_throws()
    {
        byte[] data = new byte[32];
        for (int i = 0; i < 4; i++) data[i] = (byte)"mAB "[i];
        data[8] = 3; data[9] = 3;
        // All offsets zero, including B
        Assert.Throws<IccParseException>(() => TagElementReader.Parse(data));
    }

    // --- mAB with A curves + CLUT + B curves -----------------------------

    [Fact]
    public void MAB_with_A_CLUT_and_B_curves()
    {
        // i=2, o=3. A curves count = 2. B curves count = 3. CLUT: 2×2 grid, 2 inputs, 3 outputs.
        // Layout:
        //   0..31    fixed header
        //   32..55   A curves: 2 × 12 = 24 bytes  → A starts at 32
        //   56..67   B curve 1                    → B starts at 56 (3 × 12 = 36; ends at 92)
        //   ...
        //   92..(92+20+12)  CLUT block            → CLUT starts at 92
        //     CLUT header 20 bytes + values 4*3=12 bytes (8-bit precision, 4 entries × 3 channels)
        // Total = 124 bytes.

        int aOff = 32;
        int bOff = aOff + 2 * 12;       // 56
        int clutOff = bOff + 3 * 12;    // 92
        int clutBodyLen = 4 * 3;        // 4 grid points × 3 output channels, 8-bit
        int total = clutOff + 20 + clutBodyLen;

        byte[] data = new byte[total];
        for (int i = 0; i < 4; i++) data[i] = (byte)"mAB "[i];
        data[8] = 2; data[9] = 3;
        Array.Copy(U32Be((uint)bOff), 0, data, 12, 4);    // B offset
        // matrix/M absent (zero)
        Array.Copy(U32Be((uint)clutOff), 0, data, 24, 4); // CLUT offset
        Array.Copy(U32Be((uint)aOff), 0, data, 28, 4);    // A offset

        // 2 identity A curves
        for (int c = 0; c < 2; c++)
            Buffer.BlockCopy(IdentityCurv(), 0, data, aOff + 12 * c, 12);
        // 3 identity B curves
        for (int c = 0; c < 3; c++)
            Buffer.BlockCopy(IdentityCurv(), 0, data, bOff + 12 * c, 12);

        // CLUT header: 16 bytes grid points (only first 2 used = {2, 2}); precision=1; 3 reserved.
        data[clutOff + 0] = 2;
        data[clutOff + 1] = 2;
        data[clutOff + 16] = 1; // precision = 1 (8-bit)

        // CLUT values: 4 entries × 3 channels = 12 bytes. Fill with recognizable pattern.
        for (int i = 0; i < clutBodyLen; i++) data[clutOff + 20 + i] = (byte)(i * 17);

        LutAToBTagElement t = Assert.IsType<LutAToBTagElement>(TagElementReader.Parse(data));
        Assert.Equal(2, t.InputChannels);
        Assert.Equal(3, t.OutputChannels);
        Assert.NotNull(t.ACurves);
        Assert.Equal(2, t.ACurves!.Count);
        Assert.Equal(3, t.BCurves.Count);
        Assert.NotNull(t.Clut);
        Assert.Equal(1, t.Clut!.Precision);
        Assert.Equal(2, t.Clut.GridPoints.Count);
        Assert.Equal(2, t.Clut.GridPoints[0]);
        Assert.Equal(2, t.Clut.GridPoints[1]);
        Assert.Equal(3, t.Clut.OutputChannels);
        Assert.Equal(12, t.Clut.Values.Count);
        Assert.Equal(0.0, t.Clut.Values[0], 6);          // 0/255
        Assert.Equal(17.0 / 255.0, t.Clut.Values[1], 6); // 17/255
    }

    // --- mBA: A/B curve counts swap --------------------------------------

    [Fact]
    public void MBA_assigns_A_count_to_o_and_B_count_to_i()
    {
        // i=3 (PCS), o=2 (device). For mBA: B count = i = 3, A count = o = 2.
        int bOff = 32;
        int aOff = bOff + 3 * 12; // 68
        int total = aOff + 2 * 12; // 92

        byte[] data = new byte[total];
        for (int i = 0; i < 4; i++) data[i] = (byte)"mBA "[i];
        data[8] = 3; data[9] = 2;
        Array.Copy(U32Be((uint)bOff), 0, data, 12, 4);
        Array.Copy(U32Be((uint)aOff), 0, data, 28, 4);

        for (int c = 0; c < 3; c++)
            Buffer.BlockCopy(IdentityCurv(), 0, data, bOff + 12 * c, 12);
        for (int c = 0; c < 2; c++)
            Buffer.BlockCopy(IdentityCurv(), 0, data, aOff + 12 * c, 12);

        LutBToATagElement t = Assert.IsType<LutBToATagElement>(TagElementReader.Parse(data));
        Assert.Equal(3, t.InputChannels);
        Assert.Equal(2, t.OutputChannels);
        Assert.Equal(3, t.BCurves.Count);
        Assert.NotNull(t.ACurves);
        Assert.Equal(2, t.ACurves!.Count);
    }

    // --- Matrix block ----------------------------------------------------

    [Fact]
    public void MAB_matrix_block_reads_12_s15Fixed16_values()
    {
        // i=3, o=3, B curves at 80, matrix at 32. Matrix = identity 3×3 with offset [0.1, 0.2, 0.3].
        int matOff = 32;
        int bOff = matOff + 48; // 80
        int total = bOff + 3 * 12;
        byte[] data = new byte[total];
        for (int i = 0; i < 4; i++) data[i] = (byte)"mAB "[i];
        data[8] = 3; data[9] = 3;
        Array.Copy(U32Be((uint)bOff), 0, data, 12, 4);
        Array.Copy(U32Be((uint)matOff), 0, data, 16, 4); // matrix offset

        WriteS15Fixed16(data, matOff +  0, 1.0);
        WriteS15Fixed16(data, matOff +  4, 0.0);
        WriteS15Fixed16(data, matOff +  8, 0.0);
        WriteS15Fixed16(data, matOff + 12, 0.0);
        WriteS15Fixed16(data, matOff + 16, 1.0);
        WriteS15Fixed16(data, matOff + 20, 0.0);
        WriteS15Fixed16(data, matOff + 24, 0.0);
        WriteS15Fixed16(data, matOff + 28, 0.0);
        WriteS15Fixed16(data, matOff + 32, 1.0);
        WriteS15Fixed16(data, matOff + 36, 0.1);
        WriteS15Fixed16(data, matOff + 40, 0.2);
        WriteS15Fixed16(data, matOff + 44, 0.3);

        for (int c = 0; c < 3; c++)
            Buffer.BlockCopy(IdentityCurv(), 0, data, bOff + 12 * c, 12);

        LutAToBTagElement t = Assert.IsType<LutAToBTagElement>(TagElementReader.Parse(data));
        Assert.NotNull(t.Matrix);
        Assert.Equal(12, t.Matrix!.Length);
        Assert.Equal(1.0, t.Matrix[0], 4);
        Assert.Equal(1.0, t.Matrix[4], 4);
        Assert.Equal(1.0, t.Matrix[8], 4);
        Assert.Equal(0.1, t.Matrix[9], 4);
        Assert.Equal(0.2, t.Matrix[10], 4);
        Assert.Equal(0.3, t.Matrix[11], 4);
    }

    // --- Parametric curves inside mAB ------------------------------------

    [Fact]
    public void MAB_with_parametric_B_curves()
    {
        // 3 parametric type-0 (gamma) B curves, each 16 bytes (12 header + 4 param).
        int bOff = 32;
        int total = bOff + 3 * 16;
        byte[] data = new byte[total];
        for (int i = 0; i < 4; i++) data[i] = (byte)"mAB "[i];
        data[8] = 3; data[9] = 3;
        Array.Copy(U32Be((uint)bOff), 0, data, 12, 4);

        for (int c = 0; c < 3; c++)
        {
            int p = bOff + 16 * c;
            for (int i = 0; i < 4; i++) data[p + i] = (byte)"para"[i];
            // reserved 4..7 = 0
            data[p + 8] = 0; data[p + 9] = 0; // function type 0
            // reserved 10..11 = 0
            WriteS15Fixed16(data, p + 12, 2.2); // gamma 2.2
        }

        LutAToBTagElement t = Assert.IsType<LutAToBTagElement>(TagElementReader.Parse(data));
        Assert.Equal(3, t.BCurves.Count);
        ParametricCurveTagElement first = Assert.IsType<ParametricCurveTagElement>(t.BCurves[0]);
        Assert.Equal(0, first.FunctionType);
        Assert.Equal(2.2, first.Parameters[0], 3);
    }

    // --- CLUT precision validation ---------------------------------------

    [Fact]
    public void Clut_precision_other_than_1_or_2_throws()
    {
        int clutOff = 32;
        int bOff = clutOff + 20 + 12; // CLUT body + B start (won't be reached due to throw, but layout sane)
        int total = bOff + 3 * 12;
        byte[] data = new byte[total];
        for (int i = 0; i < 4; i++) data[i] = (byte)"mAB "[i];
        data[8] = 2; data[9] = 3;
        Array.Copy(U32Be((uint)bOff), 0, data, 12, 4);
        Array.Copy(U32Be((uint)clutOff), 0, data, 24, 4);

        data[clutOff + 0] = 2; data[clutOff + 1] = 2;
        data[clutOff + 16] = 99; // invalid precision

        for (int c = 0; c < 3; c++)
            Buffer.BlockCopy(IdentityCurv(), 0, data, bOff + 12 * c, 12);

        Assert.Throws<IccParseException>(() => TagElementReader.Parse(data));
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
