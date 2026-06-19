using ICCSharp.IO;

namespace ICCSharp.Tests.IO;

public class IccBinaryReaderTests
{
    [Fact]
    public void ReadUInt8_advances_one_byte()
    {
        IccBinaryReader r = new(new byte[] { 0x2A, 0xFF });
        Assert.Equal((byte)0x2A, r.ReadUInt8());
        Assert.Equal(1, r.Position);
        Assert.Equal((byte)0xFF, r.ReadUInt8());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void ReadInt8_handles_sign()
    {
        IccBinaryReader r = new(new byte[] { 0x80, 0x7F });
        Assert.Equal((sbyte)-128, r.ReadInt8());
        Assert.Equal((sbyte)127, r.ReadInt8());
    }

    [Fact]
    public void ReadUInt16_is_big_endian()
    {
        IccBinaryReader r = new(new byte[] { 0x01, 0x02 });
        Assert.Equal((ushort)0x0102, r.ReadUInt16());
    }

    [Fact]
    public void ReadInt16_is_big_endian_signed()
    {
        IccBinaryReader r = new(new byte[] { 0xFF, 0xFE });
        Assert.Equal((short)-2, r.ReadInt16());
    }

    [Fact]
    public void ReadUInt32_is_big_endian()
    {
        IccBinaryReader r = new(new byte[] { 0x01, 0x02, 0x03, 0x04 });
        Assert.Equal(0x01020304u, r.ReadUInt32());
    }

    [Fact]
    public void ReadInt32_is_big_endian_signed()
    {
        IccBinaryReader r = new(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
        Assert.Equal(-1, r.ReadInt32());
    }

    [Fact]
    public void ReadUInt64_is_big_endian()
    {
        IccBinaryReader r = new(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 });
        Assert.Equal(0x0102030405060708ul, r.ReadUInt64());
    }

    [Fact]
    public void ReadInt64_is_big_endian_signed()
    {
        IccBinaryReader r = new(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFE });
        Assert.Equal(-2L, r.ReadInt64());
    }

    [Fact]
    public void ReadFloat32_is_big_endian()
    {
        // 1.0f in IEEE 754 single precision = 0x3F800000
        IccBinaryReader r = new(new byte[] { 0x3F, 0x80, 0x00, 0x00 });
        Assert.Equal(1.0f, r.ReadFloat32());
    }

    // ---- Fixed point ----------------------------------------------------

    [Theory]
    [InlineData(new byte[] { 0x00, 0x01, 0x00, 0x00 }, 1.0)]
    [InlineData(new byte[] { 0x00, 0x00, 0x80, 0x00 }, 0.5)]
    [InlineData(new byte[] { 0xFF, 0xFF, 0x00, 0x00 }, -1.0)]
    [InlineData(new byte[] { 0x80, 0x00, 0x00, 0x00 }, -32768.0)]
    [InlineData(new byte[] { 0x7F, 0xFF, 0x00, 0x00 }, 32767.0)]
    public void ReadS15Fixed16_matches_spec_reference_values(byte[] bytes, double expected)
    {
        IccBinaryReader r = new(bytes);
        Assert.Equal(expected, r.ReadS15Fixed16(), 6);
    }

    [Fact]
    public void ReadS15Fixed16_max_precision()
    {
        // 0x7FFFFFFF = 32767 + 65535/65536 ≈ 32767.99998474
        IccBinaryReader r = new(new byte[] { 0x7F, 0xFF, 0xFF, 0xFF });
        Assert.Equal(32767.99998474, r.ReadS15Fixed16(), 8);
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x01, 0x00, 0x00 }, 1.0)]
    [InlineData(new byte[] { 0x00, 0x00, 0x80, 0x00 }, 0.5)]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, 65535.99998474)]
    public void ReadU16Fixed16_matches_spec_reference_values(byte[] bytes, double expected)
    {
        IccBinaryReader r = new(bytes);
        Assert.Equal(expected, r.ReadU16Fixed16(), 6);
    }

    [Theory]
    [InlineData(new byte[] { 0x80, 0x00 }, 1.0)]
    [InlineData(new byte[] { 0x40, 0x00 }, 0.5)]
    [InlineData(new byte[] { 0xFF, 0xFF }, 1.99996948)]
    public void ReadU1Fixed15_matches_spec_reference_values(byte[] bytes, double expected)
    {
        IccBinaryReader r = new(bytes);
        Assert.Equal(expected, r.ReadU1Fixed15(), 6);
    }

    [Theory]
    [InlineData(new byte[] { 0x01, 0x00 }, 1.0)]
    [InlineData(new byte[] { 0x00, 0x80 }, 0.5)]
    [InlineData(new byte[] { 0xFF, 0xFF }, 255.99609375)]
    public void ReadU8Fixed8_matches_spec_reference_values(byte[] bytes, double expected)
    {
        IccBinaryReader r = new(bytes);
        Assert.Equal(expected, r.ReadU8Fixed8(), 6);
    }

    // ---- Composite primitives ------------------------------------------

    [Fact]
    public void ReadSignature_packs_four_ascii_characters_big_endian()
    {
        // 'a','c','s','p' = 0x61 0x63 0x73 0x70 (profile header magic per §7.2)
        IccBinaryReader r = new(new byte[] { 0x61, 0x63, 0x73, 0x70 });
        IccSignature sig = r.ReadSignature();
        Assert.Equal("acsp", sig.ToString());
        Assert.Equal(IccSignature.FromAscii("acsp"), sig);
    }

    [Fact]
    public void ReadXyz_three_s15Fixed16_components()
    {
        // D50 illuminant reference: X=0.9642, Y=1.0000, Z=0.8249 (approx)
        // 0.9642 * 65536 ≈ 63190 = 0x0000F6D6
        // 1.0000 * 65536 = 65536 = 0x00010000
        // 0.8249 * 65536 ≈ 54066 = 0x0000D332
        byte[] bytes =
        {
            0x00, 0x00, 0xF6, 0xD6,
            0x00, 0x01, 0x00, 0x00,
            0x00, 0x00, 0xD3, 0x32,
        };
        IccBinaryReader r = new(bytes);
        XyzNumber xyz = r.ReadXyz();
        Assert.Equal(0.9642, xyz.X, 3);
        Assert.Equal(1.0000, xyz.Y, 4);
        Assert.Equal(0.8249, xyz.Z, 3);
    }

    [Fact]
    public void ReadDateTime_six_uint16_fields()
    {
        byte[] bytes =
        {
            0x07, 0xE8, // 2024
            0x00, 0x06, // 6
            0x00, 0x0F, // 15
            0x00, 0x0C, // 12
            0x00, 0x1E, // 30
            0x00, 0x2D, // 45
        };
        IccBinaryReader r = new(bytes);
        IccDateTime dt = r.ReadDateTime();
        Assert.Equal(new IccDateTime(2024, 6, 15, 12, 30, 45), dt);
    }

    [Fact]
    public void ReadPosition_offset_then_size()
    {
        byte[] bytes =
        {
            0x00, 0x00, 0x01, 0x00, // offset = 256
            0x00, 0x00, 0x00, 0x40, // size   = 64
        };
        IccBinaryReader r = new(bytes);
        PositionNumber p = r.ReadPosition();
        Assert.Equal(256u, p.Offset);
        Assert.Equal(64u, p.Size);
    }

    [Fact]
    public void ReadResponse16_three_fields()
    {
        byte[] bytes =
        {
            0x00, 0xFF,             // device code 255
            0x00, 0x00,             // reserved
            0x00, 0x01, 0x00, 0x00, // measurement = 1.0
        };
        IccBinaryReader r = new(bytes);
        Response16Number resp = r.ReadResponse16();
        Assert.Equal((ushort)255, resp.DeviceCode);
        Assert.Equal((ushort)0, resp.Reserved);
        Assert.Equal(1.0, resp.Measurement, 6);
    }

    [Fact]
    public void ReadAsciiString_trims_trailing_nuls()
    {
        byte[] bytes = { (byte)'h', (byte)'i', 0x00, 0x00 };
        IccBinaryReader r = new(bytes);
        Assert.Equal("hi", r.ReadAsciiString(4));
        Assert.Equal(4, r.Position);
    }

    [Fact]
    public void ReadAsciiString_preserves_internal_nuls_but_trims_only_trailing()
    {
        byte[] bytes = { (byte)'a', 0x00, (byte)'b', 0x00, 0x00 };
        IccBinaryReader r = new(bytes);
        string s = r.ReadAsciiString(5);
        Assert.Equal(3, s.Length);
        Assert.Equal('a', s[0]);
        Assert.Equal('\0', s[1]);
        Assert.Equal('b', s[2]);
    }

    // ---- Cursor / bounds -----------------------------------------------

    [Fact]
    public void Skip_advances_position()
    {
        IccBinaryReader r = new(new byte[] { 0, 1, 2, 3, 4, 5 });
        r.Skip(3);
        Assert.Equal(3, r.Position);
        Assert.Equal((byte)3, r.ReadUInt8());
    }

    [Fact]
    public void Position_setter_can_seek_anywhere_within_buffer()
    {
        IccBinaryReader r = new(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD })
        {
            Position = 2
        };
        Assert.Equal((byte)0xCC, r.ReadUInt8());
        r.Position = 0;
        Assert.Equal((byte)0xAA, r.ReadUInt8());
    }

    [Fact]
    public void Position_setter_rejects_out_of_bounds()
    {
        IccBinaryReader r = new(new byte[] { 0, 1, 2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => r.Position = 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => r.Position = -1);
    }

    [Fact]
    public void Peek_does_not_advance_cursor()
    {
        IccBinaryReader r = new(new byte[] { 1, 2, 3 });
        ReadOnlySpan<byte> p = r.Peek(2);
        Assert.Equal(1, p[0]);
        Assert.Equal(2, p[1]);
        Assert.Equal(0, r.Position);
    }

    [Fact]
    public void ReadBytes_returns_slice_and_advances()
    {
        IccBinaryReader r = new(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD });
        ReadOnlySpan<byte> got = r.ReadBytes(3);
        Assert.Equal(3, got.Length);
        Assert.Equal((byte)0xAA, got[0]);
        Assert.Equal(3, r.Position);
    }

    [Fact]
    public void ReadUInt32_at_end_of_buffer_throws_with_counts()
    {
        IccBinaryReader r = new(new byte[] { 1, 2, 3 });
        var ex = Assert.Throws<IccEndOfStreamException>(() => r.ReadUInt32());
        Assert.Equal(4, ex.Requested);
        Assert.Equal(3, ex.Available);
    }

    [Fact]
    public void ReadAsciiString_past_end_throws()
    {
        IccBinaryReader r = new(new[] { (byte)'a' });
        Assert.Throws<IccEndOfStreamException>(() => r.ReadAsciiString(4));
    }
}
