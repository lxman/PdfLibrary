using ICCSharp.IO;
using ICCSharp.Profile;
using ICCSharp.Tags;

namespace ICCSharp.Tests.Tags;

public class NumericTagTests
{
    private static byte[] WithTypeHeader(string typeSig, params byte[] payload)
    {
        byte[] buf = new byte[8 + payload.Length];
        for (int i = 0; i < 4; i++) buf[i] = (byte)typeSig[i];
        // bytes 4-7 reserved = 0
        Buffer.BlockCopy(payload, 0, buf, 8, payload.Length);
        return buf;
    }

    private static byte[] U32Be(uint v) => new[]
    {
        (byte)((v >> 24) & 0xFF), (byte)((v >> 16) & 0xFF),
        (byte)((v >> 8) & 0xFF),  (byte)(v & 0xFF),
    };

    // --- XYZType -----------------------------------------------------------

    [Fact]
    public void XyzType_with_single_xyz_parses_to_one_value()
    {
        // D50 wtpt: X=0.9642 Y=1.0000 Z=0.8249
        byte[] payload =
        [
            ..U32Be(0x0000F6D6),
            ..U32Be(0x00010000),
            ..U32Be(0x0000D332),
        ];
        TagElement el = TagElementReader.Parse(WithTypeHeader("XYZ ", payload));
        XyzTagElement xyz = Assert.IsType<XyzTagElement>(el);
        Assert.Single(xyz.Values);
        Assert.Equal(0.9642, xyz.Values[0].X, 3);
        Assert.Equal(1.0000, xyz.Values[0].Y, 4);
        Assert.Equal(0.8249, xyz.Values[0].Z, 3);
    }

    [Fact]
    public void XyzType_with_three_xyz_parses_to_three_values()
    {
        byte[] one =
        [
            ..U32Be(0x00010000), ..U32Be(0x00020000), ..U32Be(0x00030000),
            ..U32Be(0x00040000), ..U32Be(0x00050000), ..U32Be(0x00060000),
            ..U32Be(0x00070000), ..U32Be(0x00080000), ..U32Be(0x00090000),
        ];
        XyzTagElement xyz = Assert.IsType<XyzTagElement>(TagElementReader.Parse(WithTypeHeader("XYZ ", one)));
        Assert.Equal(3, xyz.Values.Count);
        Assert.Equal(new XyzNumber(1, 2, 3), xyz.Values[0]);
        Assert.Equal(new XyzNumber(4, 5, 6), xyz.Values[1]);
        Assert.Equal(new XyzNumber(7, 8, 9), xyz.Values[2]);
    }

    [Fact]
    public void XyzType_payload_not_multiple_of_12_throws()
    {
        byte[] payload = new byte[11];
        Assert.Throws<IccParseException>(() => TagElementReader.Parse(WithTypeHeader("XYZ ", payload)));
    }

    // --- sf32 (s15Fixed16 array) ------------------------------------------

    [Fact]
    public void Sf32_parses_array_of_doubles()
    {
        byte[] payload =
        [
            ..U32Be(0x00010000),                  //  1.0
            ..U32Be(0x00008000),                  //  0.5
            ..U32Be(0xFFFF0000),                  // -1.0
            ..U32Be(0x80000000),                  // -32768.0
        ];
        S15Fixed16ArrayTagElement t = Assert.IsType<S15Fixed16ArrayTagElement>(
            TagElementReader.Parse(WithTypeHeader("sf32", payload)));
        Assert.Equal(4, t.Values.Count);
        Assert.Equal(1.0, t.Values[0]);
        Assert.Equal(0.5, t.Values[1]);
        Assert.Equal(-1.0, t.Values[2]);
        Assert.Equal(-32768.0, t.Values[3]);
    }

    [Fact]
    public void Sf32_payload_not_multiple_of_4_throws()
    {
        Assert.Throws<IccParseException>(() => TagElementReader.Parse(WithTypeHeader("sf32", new byte[7])));
    }

    [Fact]
    public void Sf32_with_empty_payload_yields_zero_values()
    {
        S15Fixed16ArrayTagElement t = Assert.IsType<S15Fixed16ArrayTagElement>(
            TagElementReader.Parse(WithTypeHeader("sf32")));
        Assert.Empty(t.Values);
    }

    // --- uf32 (u16Fixed16 array) ------------------------------------------

    [Fact]
    public void Uf32_parses_array_of_doubles()
    {
        byte[] payload =
        [
            ..U32Be(0x00010000),                  // 1.0
            ..U32Be(0xFFFFFFFF),                  // 65535.99998474
        ];
        U16Fixed16ArrayTagElement t = Assert.IsType<U16Fixed16ArrayTagElement>(
            TagElementReader.Parse(WithTypeHeader("uf32", payload)));
        Assert.Equal(2, t.Values.Count);
        Assert.Equal(1.0, t.Values[0]);
        Assert.Equal(65535.99998474, t.Values[1], 6);
    }

    // --- signatureType ----------------------------------------------------

    [Fact]
    public void SignatureType_reads_one_signature()
    {
        // 'CRT ' as a signature value (e.g. tech tag indicating cathode-ray tube)
        SignatureTagElement t = Assert.IsType<SignatureTagElement>(
            TagElementReader.Parse(WithTypeHeader("sig ", (byte)'C', (byte)'R', (byte)'T', (byte)' ')));
        Assert.Equal("CRT ", t.Value.ToString());
    }

    [Fact]
    public void SignatureType_payload_under_4_bytes_throws()
    {
        Assert.Throws<IccParseException>(() => TagElementReader.Parse(WithTypeHeader("sig ", 1, 2, 3)));
    }

    // --- dateTimeType -----------------------------------------------------

    [Fact]
    public void DateTimeType_reads_one_datetime()
    {
        byte[] payload =
        [
            0x07, 0xE8, 0x00, 0x06, 0x00, 0x0F, // 2024-06-15
            0x00, 0x0C, 0x00, 0x1E, 0x00, 0x2D, // 12:30:45
        ];
        DateTimeTagElement t = Assert.IsType<DateTimeTagElement>(
            TagElementReader.Parse(WithTypeHeader("dtim", payload)));
        Assert.Equal(new IccDateTime(2024, 6, 15, 12, 30, 45), t.Value);
    }

    [Fact]
    public void DateTimeType_payload_under_12_bytes_throws()
    {
        Assert.Throws<IccParseException>(() => TagElementReader.Parse(WithTypeHeader("dtim", new byte[8])));
    }

    // --- Dispatcher fallback ---------------------------------------------

    [Fact]
    public void Unrecognized_type_signature_falls_through_to_UnknownTagElement()
    {
        byte[] payload = { 0xAA, 0xBB, 0xCC, 0xDD };
        TagElement el = TagElementReader.Parse(WithTypeHeader("zzzz", payload));
        UnknownTagElement u = Assert.IsType<UnknownTagElement>(el);
        Assert.Equal("zzzz", u.TypeSignature.ToString());
        Assert.Equal(4, u.Payload.Length);
        Assert.Equal(0xAA, u.Payload.Span[0]);
        Assert.Equal(0xDD, u.Payload.Span[3]);
    }

    [Fact]
    public void Tag_data_under_8_bytes_throws()
    {
        Assert.Throws<IccParseException>(() => TagElementReader.Parse(new byte[7]));
    }
}
