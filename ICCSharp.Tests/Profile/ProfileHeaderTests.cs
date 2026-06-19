using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Tests.Profile;

public class ProfileHeaderTests
{
    private static byte[] BuildValidHeader()
    {
        // 128-byte synthetic header per ICC.1:2010 §7.2. Values are recognizable so we can
        // assert every field independently.
        var buf = new byte[ProfileHeader.Size];

        // 0   profile size = 512 (0x00000200)
        buf[0] = 0x00; buf[1] = 0x00; buf[2] = 0x02; buf[3] = 0x00;

        // 4   preferred CMM = 'lcms'
        WriteAscii(buf, 4, "lcms");

        // 8   version = 4.3.0
        buf[8] = 0x04; buf[9] = 0x30; buf[10] = 0x00; buf[11] = 0x00;

        // 12  class = 'mntr' (display)
        WriteAscii(buf, 12, "mntr");

        // 16  data color space = 'RGB '
        WriteAscii(buf, 16, "RGB ");

        // 20  PCS = 'XYZ '
        WriteAscii(buf, 20, "XYZ ");

        // 24  date = 2024-06-15 12:30:45 (BE uint16 × 6)
        WriteUInt16(buf, 24, 2024);
        WriteUInt16(buf, 26, 6);
        WriteUInt16(buf, 28, 15);
        WriteUInt16(buf, 30, 12);
        WriteUInt16(buf, 32, 30);
        WriteUInt16(buf, 34, 45);

        // 36  magic = 'acsp'
        WriteAscii(buf, 36, "acsp");

        // 40  primary platform = 'MSFT'
        WriteAscii(buf, 40, "MSFT");

        // 44  flags = Embedded | NotIndependent = 0x00000003
        buf[44] = 0; buf[45] = 0; buf[46] = 0; buf[47] = 0x03;

        // 48  manufacturer = 'TEST'
        WriteAscii(buf, 48, "TEST");

        // 52  model = 'MODL'
        WriteAscii(buf, 52, "MODL");

        // 56  device attributes (8 BE bytes) = Matte | BlackAndWhite = bit1 | bit3 = 0x0A
        buf[56] = 0; buf[57] = 0; buf[58] = 0; buf[59] = 0;
        buf[60] = 0; buf[61] = 0; buf[62] = 0; buf[63] = 0x0A;

        // 64  rendering intent = 1 (RelativeColorimetric)
        buf[64] = 0; buf[65] = 0; buf[66] = 0; buf[67] = 0x01;

        // 68  illuminant XYZ = D50 (0.9642, 1.0000, 0.8249)
        WriteUInt32(buf, 68, 0x0000F6D6); // 0.9642
        WriteUInt32(buf, 72, 0x00010000); // 1.0000
        WriteUInt32(buf, 76, 0x0000D332); // 0.8249

        // 80  creator = 'CRTR'
        WriteAscii(buf, 80, "CRTR");

        // 84..99  profile ID = bytes 0..15
        for (var i = 0; i < 16; i++) buf[84 + i] = (byte)i;

        // 100..127 reserved = zero (already)
        return buf;
    }

    [Fact]
    public void Parse_reads_every_field_from_synthetic_header()
    {
        ProfileHeader h = ProfileHeader.Parse(BuildValidHeader());

        Assert.Equal(512u, h.ProfileSize);
        Assert.Equal("lcms", h.PreferredCmm.ToString());
        Assert.Equal(new ProfileVersion(4, 3, 0), h.Version);
        Assert.Equal(ProfileClass.Display, h.Class);
        Assert.Equal("mntr", h.RawClass.ToString());
        Assert.Equal("RGB ", h.DataColorSpace.ToString());
        Assert.Equal("XYZ ", h.ProfileConnectionSpace.ToString());
        Assert.Equal(new IccDateTime(2024, 6, 15, 12, 30, 45), h.CreationDate);
        Assert.Equal(ProfileHeader.MagicNumber, h.Magic);
        Assert.Equal(PrimaryPlatform.Microsoft, h.PrimaryPlatform);
        Assert.Equal(ProfileFlags.Embedded | ProfileFlags.NotIndependent, h.Flags);
        Assert.Equal("TEST", h.DeviceManufacturer.ToString());
        Assert.Equal("MODL", h.DeviceModel.ToString());
        Assert.Equal(DeviceAttributes.Matte | DeviceAttributes.BlackAndWhite, h.DeviceAttributes);
        Assert.Equal(RenderingIntent.RelativeColorimetric, h.RenderingIntent);
        Assert.Equal(0, h.RenderingIntentHighBits);

        Assert.Equal(0.9642, h.Illuminant.X, 3);
        Assert.Equal(1.0000, h.Illuminant.Y, 4);
        Assert.Equal(0.8249, h.Illuminant.Z, 3);

        Assert.Equal("CRTR", h.ProfileCreator.ToString());
        Assert.Equal(16, h.ProfileId.Length);
        Assert.True(h.HasProfileId);
        Assert.Equal(28, h.Reserved.Length);
    }

    [Fact]
    public void Parse_advances_reader_exactly_128_bytes()
    {
        var padded = new byte[ProfileHeader.Size + 16];
        Buffer.BlockCopy(BuildValidHeader(), 0, padded, 0, ProfileHeader.Size);
        IccBinaryReader r = new(padded);
        _ = ProfileHeader.Parse(r);
        Assert.Equal(ProfileHeader.Size, r.Position);
    }

    [Fact]
    public void Parse_throws_when_magic_missing()
    {
        byte[] buf = BuildValidHeader();
        WriteAscii(buf, 36, "XXXX");
        var ex = Assert.Throws<IccParseException>(() => ProfileHeader.Parse(buf));
        Assert.Contains("acsp", ex.Message);
    }

    [Fact]
    public void Parse_throws_when_profile_size_field_below_minimum()
    {
        byte[] buf = BuildValidHeader();
        buf[0] = 0; buf[1] = 0; buf[2] = 0; buf[3] = 0x40; // 64
        Assert.Throws<IccParseException>(() => ProfileHeader.Parse(buf));
    }

    [Fact]
    public void Parse_throws_when_buffer_shorter_than_128_bytes()
    {
        Assert.Throws<IccParseException>(() => ProfileHeader.Parse(new byte[64]));
    }

    [Fact]
    public void Parse_keeps_unknown_class_signature_as_raw()
    {
        byte[] buf = BuildValidHeader();
        WriteAscii(buf, 12, "zzzz");
        ProfileHeader h = ProfileHeader.Parse(buf);
        Assert.Equal(ProfileClass.Unknown, h.Class);
        Assert.Equal("zzzz", h.RawClass.ToString());
    }

    [Fact]
    public void Parse_keeps_unknown_platform_signature_as_raw()
    {
        byte[] buf = BuildValidHeader();
        WriteAscii(buf, 40, "zzzz");
        ProfileHeader h = ProfileHeader.Parse(buf);
        Assert.Equal(PrimaryPlatform.Unknown, h.PrimaryPlatform);
        Assert.Equal("zzzz", h.RawPlatform.ToString());
    }

    [Fact]
    public void Zero_platform_signature_means_unspecified()
    {
        byte[] buf = BuildValidHeader();
        buf[40] = 0; buf[41] = 0; buf[42] = 0; buf[43] = 0;
        ProfileHeader h = ProfileHeader.Parse(buf);
        Assert.Equal(PrimaryPlatform.Unspecified, h.PrimaryPlatform);
    }

    [Fact]
    public void Zero_profile_id_is_reported_as_unset()
    {
        byte[] buf = BuildValidHeader();
        for (var i = 0; i < 16; i++) buf[84 + i] = 0;
        ProfileHeader h = ProfileHeader.Parse(buf);
        Assert.False(h.HasProfileId);
    }

    [Fact]
    public void Rendering_intent_high_bits_preserved()
    {
        byte[] buf = BuildValidHeader();
        // Set high 16 bits to 0xABCD and intent to 2 (Saturation)
        buf[64] = 0xAB; buf[65] = 0xCD; buf[66] = 0x00; buf[67] = 0x02;
        ProfileHeader h = ProfileHeader.Parse(buf);
        Assert.Equal(RenderingIntent.Saturation, h.RenderingIntent);
        Assert.Equal(0xABCD, h.RenderingIntentHighBits);
    }

    // --- helpers ------------------------------------------------------

    private static void WriteAscii(byte[] buf, int offset, string fourChars)
    {
        for (var i = 0; i < 4; i++) buf[offset + i] = (byte)fourChars[i];
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
}
