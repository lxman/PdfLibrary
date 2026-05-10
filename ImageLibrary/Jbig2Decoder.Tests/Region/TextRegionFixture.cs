using System;
using System.IO;

namespace Jbig2Decoder.Tests.Region;

internal sealed record TextRegionFixture(
    bool SbHuff, bool SbRefine, bool SbDefPixel, int SbCombOp, bool Transposed, int RefCorner,
    ushort HuffmanFlags,
    int SbDsOffset, uint SbNumInstances, int LogSbStrips, int SbStrips,
    bool SbRTemplate, sbyte[] Sbrat,
    int Width, int Height,
    SymbolBitmap[][] Dicts,
    byte[] ArithBytes,
    int OutStride, byte[] OutBytes)
{
    public static TextRegionFixture Load(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        int p = 0;
        string magic = System.Text.Encoding.ASCII.GetString(data, 0, 4);
        if (magic != "TR01" && magic != "TR02")
            throw new InvalidDataException($"Bad magic '{magic}' in {path}");
        bool v2 = magic == "TR02";
        p += 4;

        bool sbhuff   = data[p++] != 0;
        bool sbrefine = data[p++] != 0;
        bool sbdefpix = data[p++] != 0;
        int  sbcombop = data[p++];
        bool transposed = data[p++] != 0;
        int  refcorner = data[p++];
        p += 2; // pad

        ushort huffFlags = 0;
        if (v2)
        {
            huffFlags = (ushort)BitConverter.ToUInt16(data, p);
            p += 2;
            p += 2; // pad
        }

        int sbdsoffset       = BitConverter.ToInt32(data, p); p += 4;
        uint sbnuminstances  = (uint)BitConverter.ToInt32(data, p); p += 4;
        int logsbstrips      = BitConverter.ToInt32(data, p); p += 4;
        int sbstrips         = BitConverter.ToInt32(data, p); p += 4;

        bool sbrtemplate = data[p++] != 0;
        p += 3; // pad
        var sbrat = new sbyte[4];
        for (int i = 0; i < 4; i++) sbrat[i] = (sbyte)data[p++];

        int width  = BitConverter.ToInt32(data, p); p += 4;
        int height = BitConverter.ToInt32(data, p); p += 4;
        int ndicts = BitConverter.ToInt32(data, p); p += 4;

        var dicts = new SymbolBitmap[ndicts][];
        for (int d = 0; d < ndicts; d++)
        {
            int n = BitConverter.ToInt32(data, p); p += 4;
            var arr = new SymbolBitmap[n];
            for (int i = 0; i < n; i++)
            {
                int w = BitConverter.ToInt32(data, p); p += 4;
                int h = BitConverter.ToInt32(data, p); p += 4;
                int s = BitConverter.ToInt32(data, p); p += 4;
                var bytes = new byte[s * h];
                Buffer.BlockCopy(data, p, bytes, 0, bytes.Length);
                p += bytes.Length;
                arr[i] = new SymbolBitmap(w, h, s, bytes);
            }
            dicts[d] = arr;
        }

        int alen = BitConverter.ToInt32(data, p); p += 4;
        var arith = new byte[alen];
        Buffer.BlockCopy(data, p, arith, 0, alen);
        p += alen;

        int outS = BitConverter.ToInt32(data, p); p += 4;
        var outBytes = new byte[outS * height];
        Buffer.BlockCopy(data, p, outBytes, 0, outBytes.Length);

        return new TextRegionFixture(sbhuff, sbrefine, sbdefpix, sbcombop, transposed, refcorner,
            huffFlags,
            sbdsoffset, sbnuminstances, logsbstrips, sbstrips,
            sbrtemplate, sbrat,
            width, height, dicts, arith, outS, outBytes);
    }
}
