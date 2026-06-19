using System.Text;
using ICCSharp.Profile;
using ICCSharp.Tags;

namespace ICCSharp.Tests.Tags;

public class AdvancedTagTests
{
    private static byte[] WithHeader(string typeSig, params byte[] payload)
    {
        var buf = new byte[8 + payload.Length];
        for (var i = 0; i < 4; i++) buf[i] = (byte)typeSig[i];
        Buffer.BlockCopy(payload, 0, buf, 8, payload.Length);
        return buf;
    }

    private static byte[] U16Be(ushort v) => new[] { (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF) };
    private static byte[] U32Be(uint v) => new[]
    {
        (byte)((v >> 24) & 0xFF), (byte)((v >> 16) & 0xFF),
        (byte)((v >> 8) & 0xFF),  (byte)(v & 0xFF),
    };
    private static byte[] U32BeS15F16(double v)
    {
        var raw = (int)Math.Round(v * 65536.0);
        return U32Be(unchecked((uint)raw));
    }
    private static byte[] U32BeU16F16(double v)
    {
        var raw = (uint)Math.Round(v * 65536.0);
        return U32Be(raw);
    }

    // --- chromaticityType -------------------------------------------------

    [Fact]
    public void Chromaticity_with_three_channels()
    {
        // 3 channels, phosphor type = 1 (ITU-R BT.709). sRGB primaries.
        byte[] payload =
        [
            ..U16Be(3),
            ..U16Be(1),
            ..U32BeU16F16(0.6400), ..U32BeU16F16(0.3300), // R
            ..U32BeU16F16(0.3000), ..U32BeU16F16(0.6000), // G
            ..U32BeU16F16(0.1500), ..U32BeU16F16(0.0600), // B
        ];
        var t = Assert.IsType<ChromaticityTagElement>(
            TagElementReader.Parse(WithHeader("chrm", payload)));
        Assert.Equal(3, t.DeviceChannels);
        Assert.Equal((ushort)1, t.PhosphorOrColorantType);
        Assert.Equal(3, t.Coordinates.Count);
        Assert.Equal(0.6400, t.Coordinates[0].X, 3);
        Assert.Equal(0.3300, t.Coordinates[0].Y, 3);
        Assert.Equal(0.0600, t.Coordinates[2].Y, 3);
    }

    [Fact]
    public void Chromaticity_truncated_payload_throws()
    {
        byte[] payload = [..U16Be(3), ..U16Be(0), ..U32Be(0)]; // declares 3 channels but only 4 bytes follow
        Assert.Throws<IccParseException>(() => TagElementReader.Parse(WithHeader("chrm", payload)));
    }

    // --- cicpType ---------------------------------------------------------

    [Fact]
    public void Cicp_reads_four_bytes()
    {
        // BT.2020 / PQ / RGB / full range
        byte[] payload = { 9, 16, 0, 1 };
        var t = Assert.IsType<CicpTagElement>(TagElementReader.Parse(WithHeader("cicp", payload)));
        Assert.Equal(9, t.ColourPrimaries);
        Assert.Equal(16, t.TransferCharacteristics);
        Assert.Equal(0, t.MatrixCoefficients);
        Assert.Equal(1, t.VideoFullRangeFlag);
    }

    [Fact]
    public void Cicp_payload_under_4_bytes_throws()
    {
        Assert.Throws<IccParseException>(() => TagElementReader.Parse(WithHeader("cicp", 1, 2, 3)));
    }

    // --- measurementType --------------------------------------------------

    [Fact]
    public void Measurement_reads_all_fields()
    {
        byte[] payload =
        [
            ..U32Be(1),                                              // observer = CIE 1931
            ..U32BeS15F16(0.9642), ..U32BeS15F16(1.0), ..U32BeS15F16(0.8249), // backing XYZ = D50
            ..U32Be(2),                                              // geometry = 0°/d
            ..U32BeU16F16(0.005),                                    // flare
            ..U32Be(1),                                              // illuminant = D50
        ];
        var t = Assert.IsType<MeasurementTagElement>(
            TagElementReader.Parse(WithHeader("meas", payload)));
        Assert.Equal(1u, t.StandardObserver);
        Assert.Equal(0.9642, t.BackingMeasurement.X, 3);
        Assert.Equal(2u, t.MeasurementGeometry);
        Assert.Equal(0.005, t.MeasurementFlare, 3);
        Assert.Equal(1u, t.StandardIlluminant);
    }

    // --- viewingConditionsType -------------------------------------------

    [Fact]
    public void Viewing_conditions_reads_illuminant_surround_type()
    {
        byte[] payload =
        [
            ..U32BeS15F16(0.9642), ..U32BeS15F16(1.0), ..U32BeS15F16(0.8249),
            ..U32BeS15F16(0.1928), ..U32BeS15F16(0.2),  ..U32BeS15F16(0.165),
            ..U32Be(1),
        ];
        var t = Assert.IsType<ViewingConditionsTagElement>(
            TagElementReader.Parse(WithHeader("view", payload)));
        Assert.Equal(0.9642, t.Illuminant.X, 3);
        Assert.Equal(0.2, t.Surround.Y, 3);
        Assert.Equal(1u, t.IlluminantType);
    }

    // --- colorantTableType ------------------------------------------------

    [Fact]
    public void Colorant_table_with_two_entries()
    {
        var name1 = new byte[32]; Encoding.ASCII.GetBytes("Cyan").CopyTo(name1, 0);
        var name2 = new byte[32]; Encoding.ASCII.GetBytes("Magenta").CopyTo(name2, 0);
        byte[] payload =
        [
            ..U32Be(2),
            ..name1, ..U16Be(0x1000), ..U16Be(0x2000), ..U16Be(0x3000),
            ..name2, ..U16Be(0x4000), ..U16Be(0x5000), ..U16Be(0x6000),
        ];
        var t = Assert.IsType<ColorantTableTagElement>(
            TagElementReader.Parse(WithHeader("clrt", payload)));
        Assert.Equal(2, t.Entries.Count);
        Assert.Equal("Cyan", t.Entries[0].Name);
        Assert.Equal(0x1000, t.Entries[0].PCS1);
        Assert.Equal("Magenta", t.Entries[1].Name);
        Assert.Equal(0x6000, t.Entries[1].PCS3);
    }

    [Fact]
    public void Colorant_table_truncated_throws()
    {
        byte[] payload = [..U32Be(5)]; // claims 5 entries with no data
        Assert.Throws<IccParseException>(() => TagElementReader.Parse(WithHeader("clrt", payload)));
    }

    // --- colorantOrderType ------------------------------------------------

    [Fact]
    public void Colorant_order_reads_index_array()
    {
        byte[] payload = [..U32Be(4), 3, 0, 1, 2];
        var t = Assert.IsType<ColorantOrderTagElement>(
            TagElementReader.Parse(WithHeader("clro", payload)));
        Assert.Equal(4, t.Order.Count);
        Assert.Equal(3, t.Order[0]);
        Assert.Equal(2, t.Order[3]);
    }

    [Fact]
    public void Colorant_order_count_exceeding_payload_throws()
    {
        byte[] payload = [..U32Be(100), 1, 2]; // 100 entries declared, 2 supplied
        Assert.Throws<IccParseException>(() => TagElementReader.Parse(WithHeader("clro", payload)));
    }

    // --- namedColor2Type --------------------------------------------------

    [Fact]
    public void NamedColor2_with_cmyk_palette_one_entry()
    {
        var prefix = new byte[32]; Encoding.ASCII.GetBytes("PANTONE").CopyTo(prefix, 0);
        var suffix = new byte[32]; // empty
        var entryName = new byte[32]; Encoding.ASCII.GetBytes("185 C").CopyTo(entryName, 0);

        byte[] payload =
        [
            ..U32Be(0),     // vendor flag
            ..U32Be(1),     // 1 named color
            ..U32Be(4),     // 4 device coords (CMYK)
            ..prefix,
            ..suffix,
            ..entryName,
            ..U16Be(0x1234), ..U16Be(0x5678), ..U16Be(0x9ABC),     // PCS coords
            ..U16Be(0x0000), ..U16Be(0xFFFF), ..U16Be(0x1111), ..U16Be(0x2222), // device CMYK
        ];

        var t = Assert.IsType<NamedColor2TagElement>(
            TagElementReader.Parse(WithHeader("ncl2", payload)));
        Assert.Equal(0u, t.VendorFlag);
        Assert.Equal(4u, t.DeviceCoordCount);
        Assert.Equal("PANTONE", t.Prefix);
        Assert.Equal(string.Empty, t.Suffix);
        Assert.Single(t.Entries);
        Assert.Equal("185 C", t.Entries[0].Name);
        Assert.Equal(3, t.Entries[0].PcsCoords.Length);
        Assert.Equal(0x1234, t.Entries[0].PcsCoords[0]);
        Assert.Equal(4, t.Entries[0].DeviceCoords.Length);
        Assert.Equal(0xFFFF, t.Entries[0].DeviceCoords[1]);
    }

    [Fact]
    public void NamedColor2_payload_under_76_bytes_throws()
    {
        Assert.Throws<IccParseException>(() => TagElementReader.Parse(WithHeader("ncl2", new byte[40])));
    }
}
