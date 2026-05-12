using System.Text;

namespace Jbig2Decoder.Tests.Region;

internal sealed record SymbolBitmap(int Width, int Height, int Stride, byte[] Bytes);

internal sealed record SymbolDictionaryFixture(
    bool SdHuff, bool SdRefAgg, int SdTemplate, int SdRTemplate,
    sbyte[] Sdat, sbyte[] Sdrat,
    ushort HuffmanFlags,
    uint NumIn, uint NumNew, uint NumEx,
    SymbolBitmap[] InSyms,
    byte[] ArithBytes,
    SymbolBitmap[] ExSyms)
{
    public static SymbolDictionaryFixture Load(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        var p = 0;
        string magic = Encoding.ASCII.GetString(data, 0, 4);
        if (magic != "SD01" && magic != "SD02")
            throw new InvalidDataException($"Bad magic '{magic}' in {path}");
        bool v2 = magic == "SD02";
        p += 4;

        bool sdhuff = data[p++] != 0;
        bool sdrefagg = data[p++] != 0;
        int sdtemplate = data[p++];
        int sdrtemplate = data[p++];

        var sdat = new sbyte[8];
        for (var i = 0; i < 8; i++) sdat[i] = (sbyte)data[p++];
        var sdrat = new sbyte[4];
        for (var i = 0; i < 4; i++) sdrat[i] = (sbyte)data[p++];
        p += 3; // pad

        ushort huffFlags = 0;
        if (v2)
        {
            huffFlags = BitConverter.ToUInt16(data, p);
            p += 2;
            p += 2; // pad
        }

        var nIn  = (uint)BitConverter.ToInt32(data, p); p += 4;
        var nNew = (uint)BitConverter.ToInt32(data, p); p += 4;
        var nEx  = (uint)BitConverter.ToInt32(data, p); p += 4;

        var inSyms = new SymbolBitmap[nIn];
        for (var s = 0; s < nIn; s++)
            inSyms[s] = ReadSymbol(data, ref p);

        var alen = BitConverter.ToInt32(data, p); p += 4;
        var arith = new byte[alen];
        Buffer.BlockCopy(data, p, arith, 0, alen);
        p += alen;

        var exSyms = new SymbolBitmap[nEx];
        for (var s = 0; s < nEx; s++)
            exSyms[s] = ReadSymbol(data, ref p);

        return new SymbolDictionaryFixture(sdhuff, sdrefagg, sdtemplate, sdrtemplate,
            sdat, sdrat, huffFlags, nIn, nNew, nEx, inSyms, arith, exSyms);
    }

    private static SymbolBitmap ReadSymbol(byte[] data, ref int p)
    {
        var w = BitConverter.ToInt32(data, p); p += 4;
        var h = BitConverter.ToInt32(data, p); p += 4;
        var s = BitConverter.ToInt32(data, p); p += 4;
        var bytes = new byte[s * h];
        Buffer.BlockCopy(data, p, bytes, 0, bytes.Length);
        p += bytes.Length;
        return new SymbolBitmap(w, h, s, bytes);
    }
}
