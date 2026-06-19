using ICCSharp.Profile;
using ICCSharp.Tags;

namespace ICCSharp.Tests.Tags;

public class LegacyLutTagTests
{
    private static byte[] WithHeader(string typeSig, byte[] payload)
    {
        var buf = new byte[8 + payload.Length];
        for (var i = 0; i < 4; i++) buf[i] = (byte)typeSig[i];
        Buffer.BlockCopy(payload, 0, buf, 8, payload.Length);
        return buf;
    }

    private static byte[] U16Be(ushort v) => new[] { (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF) };

    // --- mft1 -------------------------------------------------------------

    [Fact]
    public void Mft1_parses_3_to_3_with_grid_2_identity_matrix()
    {
        // i=3, o=3, g=2 → CLUT entries = 2^3 = 8; clut bytes = 8 × 3 = 24.
        // Input tables: 256 × 3 = 768 bytes. Output: 256 × 3 = 768.
        // Fixed header in payload: 4 + 36 = 40.
        // Total payload: 40 + 768 + 24 + 768 = 1600.
        var payload = new byte[1600];
        payload[0] = 3; // i
        payload[1] = 3; // o
        payload[2] = 2; // g
        payload[3] = 0; // reserved

        // Identity matrix: e11=e22=e33=1, others 0. s15Fixed16(1) = 0x00010000.
        WriteS15Fixed16(payload, 4 + 0 * 4, 1);
        WriteS15Fixed16(payload, 4 + 4 * 4, 1);
        WriteS15Fixed16(payload, 4 + 8 * 4, 1);

        // Fill input tables with identity ramp (i / 255 → i). Bytes already 0-init; set each
        // 256-entry table to 0,1,2,...,255 per channel for channel 0; leave others zero for the test.
        for (var i = 0; i < 256; i++) payload[40 + i] = (byte)i;

        // Fill CLUT with recognizable values: 0,1,2,...
        for (var i = 0; i < 24; i++) payload[40 + 768 + i] = (byte)i;

        // Output tables — leave zero; just need them present.

        var t = Assert.IsType<Lut8TagElement>(TagElementReader.Parse(WithHeader("mft1", payload)));
        Assert.Equal(3, t.InputChannels);
        Assert.Equal(3, t.OutputChannels);
        Assert.Equal(2, t.ClutGridPoints);
        Assert.Equal(1.0, t.Matrix[0], 6);
        Assert.Equal(1.0, t.Matrix[4], 6);
        Assert.Equal(1.0, t.Matrix[8], 6);
        Assert.Equal(0.0, t.Matrix[1], 6);

        Assert.Equal(3, t.InputTables.Length);
        Assert.Equal(256, t.InputTables[0].Length);
        Assert.Equal(0, t.InputTables[0][0]);
        Assert.Equal(255, t.InputTables[0][255]);

        Assert.Equal(24, t.Clut.Length);
        Assert.Equal(0, t.Clut[0]);
        Assert.Equal(23, t.Clut[23]);

        Assert.Equal(3, t.OutputTables.Length);
        Assert.Equal(256, t.OutputTables[0].Length);
    }

    [Fact]
    public void Mft1_truncated_payload_throws()
    {
        // Declare i=3, o=3, g=17 (huge CLUT), but supply only the fixed header.
        var payload = new byte[40];
        payload[0] = 3; payload[1] = 3; payload[2] = 17;
        Assert.Throws<IccParseException>(() => TagElementReader.Parse(WithHeader("mft1", payload)));
    }

    [Fact]
    public void Mft1_header_under_40_bytes_throws()
    {
        Assert.Throws<IccParseException>(() => TagElementReader.Parse(WithHeader("mft1", new byte[20])));
    }

    // --- mft2 -------------------------------------------------------------

    [Fact]
    public void Mft2_parses_3_to_3_with_grid_2()
    {
        int n = 2, m = 2; // smallest legal table sizes
        int i = 3, o = 3, g = 2;
        long clutEntries = 8; // 2^3
        // bytes: 44 fixed + 2*n*i + 2*clut*o + 2*m*o
        //      = 44 + 12 + 48 + 12 = 116
        int payloadLen = 44 + (2 * n * i) + (int)(2 * clutEntries * o) + (2 * m * o);
        var payload = new byte[payloadLen];

        payload[0] = (byte)i;
        payload[1] = (byte)o;
        payload[2] = (byte)g;
        payload[3] = 0;
        WriteS15Fixed16(payload, 4 + 0 * 4, 1);
        WriteS15Fixed16(payload, 4 + 4 * 4, 1);
        WriteS15Fixed16(payload, 4 + 8 * 4, 1);

        WriteUInt16(payload, 40, (ushort)n);
        WriteUInt16(payload, 42, (ushort)m);

        // Input tables: per channel, n entries, each 16-bit. Channel 0: 0, 65535. Others left zero.
        WriteUInt16(payload, 44, 0);
        WriteUInt16(payload, 46, 0xFFFF);

        // CLUT: fill ascending uint16 values 0, 256, 512, ...
        for (var k = 0; k < clutEntries * o; k++)
            WriteUInt16(payload, 44 + 2 * n * i + 2 * k, (ushort)(k * 256));

        var t = Assert.IsType<Lut16TagElement>(TagElementReader.Parse(WithHeader("mft2", payload)));
        Assert.Equal(3, t.InputChannels);
        Assert.Equal(3, t.OutputChannels);
        Assert.Equal(2, t.ClutGridPoints);
        Assert.Equal(2, t.InputTableEntries);
        Assert.Equal(2, t.OutputTableEntries);
        Assert.Equal(0xFFFF, t.InputTables[0][1]);
        Assert.Equal(24, t.Clut.Length);
        Assert.Equal(0, t.Clut[0]);
        Assert.Equal(23 * 256, t.Clut[23]);
    }

    [Fact]
    public void Mft2_rejects_table_size_below_two()
    {
        var payload = new byte[44];
        payload[0] = 1; payload[1] = 1; payload[2] = 2;
        WriteUInt16(payload, 40, 1); // n = 1
        WriteUInt16(payload, 42, 2);
        Assert.Throws<IccParseException>(() => TagElementReader.Parse(WithHeader("mft2", payload)));
    }

    [Fact]
    public void Mft2_header_under_44_bytes_throws()
    {
        Assert.Throws<IccParseException>(() => TagElementReader.Parse(WithHeader("mft2", new byte[30])));
    }

    // --- helpers ----------------------------------------------------------

    private static void WriteUInt16(byte[] buf, int offset, ushort value)
    {
        buf[offset]     = (byte)((value >> 8) & 0xFF);
        buf[offset + 1] = (byte)(value & 0xFF);
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
