using ICCSharp.Eval;
using ICCSharp.Tags;

namespace ICCSharp.Tests.Eval;

public class LegacyLutPipelineTests
{
    // ---- mft1 (lut8) ---------------------------------------------------

    [Fact]
    public void Mft1_identity_pipeline_returns_input()
    {
        // 3 → 3, 2 grid points, identity tables, identity CLUT.
        int i = 3, o = 3, g = 2;
        double[] matrix = { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        byte[][] inputTables = new byte[i][];
        for (int c = 0; c < i; c++)
        {
            inputTables[c] = new byte[256];
            for (int k = 0; k < 256; k++) inputTables[c][k] = (byte)k;
        }

        // Identity CLUT: each grid point's output equals the grid coordinate.
        byte[] clut = new byte[g * g * g * o];
        int idx = 0;
        for (int r = 0; r < g; r++)
        for (int gp = 0; gp < g; gp++)
        for (int bp = 0; bp < g; bp++)
        {
            clut[idx++] = (byte)(r * 255);
            clut[idx++] = (byte)(gp * 255);
            clut[idx++] = (byte)(bp * 255);
        }

        byte[][] outputTables = new byte[o][];
        for (int c = 0; c < o; c++)
        {
            outputTables[c] = new byte[256];
            for (int k = 0; k < 256; k++) outputTables[c][k] = (byte)k;
        }

        Lut8TagElement tag = new(i, o, g, matrix, inputTables, clut, outputTables);
        LegacyLutPipeline pipe = new(tag);

        Assert.Equal(3, pipe.InputChannels);
        Assert.Equal(3, pipe.OutputChannels);
        double[] result = new double[3];
        pipe.Apply(new[] { 0.25, 0.5, 0.75 }, result);
        Assert.Equal(0.25, result[0], 3);
        Assert.Equal(0.5,  result[1], 3);
        Assert.Equal(0.75, result[2], 3);
    }

    // ---- mft2 (lut16) --------------------------------------------------

    [Fact]
    public void Mft2_identity_pipeline_returns_input()
    {
        int i = 3, o = 3, g = 2;
        double[] matrix = { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        int n = 256, m = 256;

        ushort[][] inputTables = new ushort[i][];
        for (int c = 0; c < i; c++)
        {
            inputTables[c] = new ushort[n];
            for (int k = 0; k < n; k++) inputTables[c][k] = (ushort)(k * 65535 / (n - 1));
        }

        ushort[] clut = new ushort[g * g * g * o];
        int idx = 0;
        for (int r = 0; r < g; r++)
        for (int gp = 0; gp < g; gp++)
        for (int bp = 0; bp < g; bp++)
        {
            clut[idx++] = (ushort)(r * 65535);
            clut[idx++] = (ushort)(gp * 65535);
            clut[idx++] = (ushort)(bp * 65535);
        }

        ushort[][] outputTables = new ushort[o][];
        for (int c = 0; c < o; c++)
        {
            outputTables[c] = new ushort[m];
            for (int k = 0; k < m; k++) outputTables[c][k] = (ushort)(k * 65535 / (m - 1));
        }

        Lut16TagElement tag = new(i, o, g, matrix, n, m, inputTables, clut, outputTables);
        LegacyLutPipeline pipe = new(tag);

        double[] result = new double[3];
        pipe.Apply(new[] { 0.3, 0.5, 0.8 }, result);
        Assert.Equal(0.3, result[0], 3);
        Assert.Equal(0.5, result[1], 3);
        Assert.Equal(0.8, result[2], 3);
    }

    [Fact]
    public void Cmyk_to_3_channel_via_legacy_lut()
    {
        // 4-channel input (CMYK), 3-channel output. Matrix must not be applied (input != 3 channels).
        int i = 4, o = 3, g = 2;
        double[] matrix = { 99, 99, 99, 99, 99, 99, 99, 99, 99 }; // ridiculous values to prove non-application
        byte[][] inputTables = new byte[i][];
        for (int c = 0; c < i; c++)
        {
            inputTables[c] = new byte[256];
            for (int k = 0; k < 256; k++) inputTables[c][k] = (byte)k;
        }
        // CLUT 2^4 = 16 grid points, each with 3 outputs.
        byte[] clut = new byte[16 * o];
        // Make the CLUT output = (c+m+y)/3 in some channel — just a synthetic mapping.
        int idx = 0;
        for (int c1 = 0; c1 < 2; c1++)
        for (int c2 = 0; c2 < 2; c2++)
        for (int c3 = 0; c3 < 2; c3++)
        for (int c4 = 0; c4 < 2; c4++)
        {
            clut[idx++] = (byte)(c1 * 255);
            clut[idx++] = (byte)(c2 * 255);
            clut[idx++] = (byte)(c3 * 255);
        }
        byte[][] outputTables = new byte[o][];
        for (int c = 0; c < o; c++)
        {
            outputTables[c] = new byte[256];
            for (int k = 0; k < 256; k++) outputTables[c][k] = (byte)k;
        }

        Lut8TagElement tag = new(i, o, g, matrix, inputTables, clut, outputTables);
        LegacyLutPipeline pipe = new(tag);
        Assert.Equal(4, pipe.InputChannels);
        Assert.Equal(3, pipe.OutputChannels);

        // CMYK (1, 1, 1, 0) → CLUT corner (c1=1,c2=1,c3=1,c4=0) = (255, 255, 255) ÷ 255 = (1,1,1).
        double[] result = new double[3];
        pipe.Apply(new[] { 1.0, 1.0, 1.0, 0.0 }, result);
        Assert.Equal(1.0, result[0], 4);
        Assert.Equal(1.0, result[1], 4);
        Assert.Equal(1.0, result[2], 4);
    }

    [Fact]
    public void Mft1_input_count_mismatch_throws()
    {
        int i = 3, o = 3, g = 2;
        double[] matrix = { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        byte[][] inputTables = { new byte[256], new byte[256], new byte[256] };
        byte[] clut = new byte[g * g * g * o];
        byte[][] outputTables = { new byte[256], new byte[256], new byte[256] };
        Lut8TagElement tag = new(i, o, g, matrix, inputTables, clut, outputTables);

        LegacyLutPipeline pipe = new(tag);
        Assert.Throws<ArgumentException>(() => pipe.Apply(new[] { 0.5, 0.5 }, new double[3]));
    }
}
