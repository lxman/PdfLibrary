using System.Text;

namespace Jbig2Decoder.Tests.Region;

/// <summary>
/// Reads a generic-region fixture file produced by the patched jbig2dec
/// (see ImageLibraries/oracle-notes/dump_generic.h). Layout:
///
///   magic                 4 bytes  "GR01"
///   MMR                   1 byte
///   GBTEMPLATE            1 byte
///   TPGDON                1 byte
///   USESKIP               1 byte
///   gbat                  8 bytes  (int8_t each)
///   width                 4 bytes  little-endian uint32
///   height                4 bytes
///   arith_len             4 bytes
///   arith_bytes           arith_len bytes
///   bitmap_stride         4 bytes
///   bitmap_bytes          height * stride bytes (1 bit per pixel, MSB-first)
/// </summary>
internal sealed record GenericRegionFixture(
    bool Mmr,
    int GbTemplate,
    bool TpgdOn,
    bool UseSkip,
    sbyte[] Gbat,
    int Width,
    int Height,
    byte[] ArithBytes,
    int BitmapStride,
    byte[] BitmapBytes)
{
    public static GenericRegionFixture Load(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        var p = 0;

        string magic = Encoding.ASCII.GetString(data, 0, 4);
        if (magic != "GR01") throw new InvalidDataException($"Bad magic '{magic}' in {path}");
        p += 4;

        bool mmr     = data[p++] != 0;
        int gbt      = data[p++];
        bool tpgdon  = data[p++] != 0;
        bool useskip = data[p++] != 0;

        var gbat = new sbyte[8];
        for (var i = 0; i < 8; i++) gbat[i] = (sbyte)data[p++];

        var width  = BitConverter.ToInt32(data, p); p += 4;
        var height = BitConverter.ToInt32(data, p); p += 4;
        var alen   = BitConverter.ToInt32(data, p); p += 4;
        var arith = new byte[alen];
        Buffer.BlockCopy(data, p, arith, 0, alen);
        p += alen;

        var stride = BitConverter.ToInt32(data, p); p += 4;
        int bmpLen = stride * height;
        var bmp = new byte[bmpLen];
        Buffer.BlockCopy(data, p, bmp, 0, bmpLen);

        return new GenericRegionFixture(mmr, gbt, tpgdon, useskip, gbat, width, height, arith, stride, bmp);
    }
}
