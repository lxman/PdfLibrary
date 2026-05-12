using System.Text;

namespace Jbig2Decoder.Tests.Region;

internal sealed record RefinementRegionFixture(
    int GrTemplate, bool TpgrOn,
    int Dx, int Dy, sbyte[] Grat,
    int Width, int Height,
    int RefWidth, int RefHeight, int RefStride, byte[] RefBytes,
    byte[] ArithBytes,
    int OutStride, byte[] OutBytes)
{
    public static RefinementRegionFixture Load(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        var p = 0;
        if (Encoding.ASCII.GetString(data, 0, 4) != "RR01")
            throw new InvalidDataException($"Bad magic in {path}");
        p += 4;

        int grTemplate = data[p++];
        bool tpgrOn = data[p++] != 0;
        p += 2; // pad
        var dx = BitConverter.ToInt32(data, p); p += 4;
        var dy = BitConverter.ToInt32(data, p); p += 4;
        var grat = new sbyte[4];
        for (var i = 0; i < 4; i++) grat[i] = (sbyte)data[p++];

        var width  = BitConverter.ToInt32(data, p); p += 4;
        var height = BitConverter.ToInt32(data, p); p += 4;
        var refW   = BitConverter.ToInt32(data, p); p += 4;
        var refH   = BitConverter.ToInt32(data, p); p += 4;
        var refS   = BitConverter.ToInt32(data, p); p += 4;
        var refBytes = new byte[refS * refH];
        Buffer.BlockCopy(data, p, refBytes, 0, refBytes.Length);
        p += refBytes.Length;

        var alen = BitConverter.ToInt32(data, p); p += 4;
        var arith = new byte[alen];
        Buffer.BlockCopy(data, p, arith, 0, alen);
        p += alen;

        var outS = BitConverter.ToInt32(data, p); p += 4;
        var outBytes = new byte[outS * height];
        Buffer.BlockCopy(data, p, outBytes, 0, outBytes.Length);

        return new RefinementRegionFixture(grTemplate, tpgrOn, dx, dy, grat,
            width, height, refW, refH, refS, refBytes, arith, outS, outBytes);
    }
}
