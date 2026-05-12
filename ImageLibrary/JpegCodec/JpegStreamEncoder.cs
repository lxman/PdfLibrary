using System;
using System.Collections.Generic;
using JpegCodec.Encode;
using JpegCodec.Segments;
using JpegCodec.Stream;

namespace JpegCodec;

public sealed class JpegStreamEncoder
{
    public byte[] Encode(byte[] componentData, JpegEncodeOptions options)
    {
        if (componentData is null) throw new ArgumentNullException(nameof(componentData));
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (options.Width <= 0) throw new ArgumentException("Width must be positive.", nameof(options));
        if (options.Height <= 0) throw new ArgumentException("Height must be positive.", nameof(options));
        if (options.NumberOfComponents is not (1 or 3 or 4))
            throw new ArgumentException("NumberOfComponents must be 1, 3, or 4.", nameof(options));
        if (options.Progressive)
            throw new NotSupportedException("Progressive encoding is not in v1 scope.");

        int width = options.Width;
        int height = options.Height;
        int nc = options.NumberOfComponents;
        int expected = width * height * nc;
        if (componentData.Length < expected)
            throw new ArgumentException(
                $"componentData length {componentData.Length} too small for {width}x{height}x{nc} = {expected}.",
                nameof(componentData));

        // No subsampling for v1 — all components at H=V=1.
        const int H = 1, V = 1;

        // Quantization tables — luma uses table 0, others (chroma/CMYK) use 1.
        ushort[] lumaQ = StandardJpegTables.ScaleQuantTable(StandardJpegTables.LumaQuantBase50, options.Quality);
        ushort[] chromaQ = nc == 1
            ? lumaQ
            : StandardJpegTables.ScaleQuantTable(StandardJpegTables.ChromaQuantBase50, options.Quality);

        // Huffman canonical tables for encoding.
        var lumaDcCanonical = HuffmanCanonicalTable.Build(StandardJpegTables.LumaDcBits, StandardJpegTables.LumaDcValues);
        var lumaAcCanonical = HuffmanCanonicalTable.Build(StandardJpegTables.LumaAcBits, StandardJpegTables.LumaAcValues);
        HuffmanCanonicalTable chromaDcCanonical = nc == 1
            ? lumaDcCanonical
            : HuffmanCanonicalTable.Build(StandardJpegTables.ChromaDcBits, StandardJpegTables.ChromaDcValues);
        HuffmanCanonicalTable chromaAcCanonical = nc == 1
            ? lumaAcCanonical
            : HuffmanCanonicalTable.Build(StandardJpegTables.ChromaAcBits, StandardJpegTables.ChromaAcValues);

        var lumaDcEnc = new HuffmanEncoder(lumaDcCanonical);
        var lumaAcEnc = new HuffmanEncoder(lumaAcCanonical);
        var chromaDcEnc = new HuffmanEncoder(chromaDcCanonical);
        var chromaAcEnc = new HuffmanEncoder(chromaAcCanonical);

        // Output construction.
        var output = new List<byte>(expected / 4);

        MarkerWriter.WriteMarker(output, JpegMarker.Soi);

        if (options.EmitJfif && nc != 4)
            MarkerWriter.WriteJfif(output);

        if (options.EmitAdobeMarker)
            MarkerWriter.WriteAdobeApp14(output, options.AdobeColorTransform);

        MarkerWriter.WriteDqt(output, 0, lumaQ);
        if (nc != 1) MarkerWriter.WriteDqt(output, 1, chromaQ);

        var frameComponents = new MarkerWriter.FrameComponentSpec[nc];
        for (var c = 0; c < nc; c++)
            frameComponents[c] = new MarkerWriter.FrameComponentSpec(
                id: (byte)(c + 1),
                hSampling: H,
                vSampling: V,
                quantTableId: (byte)(c == 0 ? 0 : 1));
        MarkerWriter.WriteSof0(output, width, height, precision: 8, frameComponents);

        MarkerWriter.WriteDht(output, tableClass: 0, tableId: 0,
            StandardJpegTables.LumaDcBits, StandardJpegTables.LumaDcValues);
        MarkerWriter.WriteDht(output, tableClass: 1, tableId: 0,
            StandardJpegTables.LumaAcBits, StandardJpegTables.LumaAcValues);
        if (nc != 1)
        {
            MarkerWriter.WriteDht(output, tableClass: 0, tableId: 1,
                StandardJpegTables.ChromaDcBits, StandardJpegTables.ChromaDcValues);
            MarkerWriter.WriteDht(output, tableClass: 1, tableId: 1,
                StandardJpegTables.ChromaAcBits, StandardJpegTables.ChromaAcValues);
        }

        var scanComponents = new MarkerWriter.ScanComponentSpec[nc];
        for (var c = 0; c < nc; c++)
            scanComponents[c] = new MarkerWriter.ScanComponentSpec(
                id: (byte)(c + 1),
                dcTableId: (byte)(c == 0 ? 0 : 1),
                acTableId: (byte)(c == 0 ? 0 : 1));
        MarkerWriter.WriteSos(output, scanComponents, ss: 0, se: 63, ah: 0, al: 0);

        // Entropy-coded scan.
        var writer = new BitWriter(output);
        var dcPredictors = new int[nc];

        int mcuW = 8 * H, mcuH = 8 * V;
        int mcusPerLine = (width + mcuW - 1) / mcuW;
        int mcusPerCol = (height + mcuH - 1) / mcuH;
        for (var my = 0; my < mcusPerCol; my++)
        {
            for (var mx = 0; mx < mcusPerLine; mx++)
            {
                for (var c = 0; c < nc; c++)
                {
                    int blockX = mx;
                    int blockY = my;
                    Span<short> block = stackalloc short[64];
                    LoadBlockFromInterleaved(componentData, width, height, nc, c, blockX * 8, blockY * 8, block);

                    HuffmanEncoder dcEnc = c == 0 ? lumaDcEnc : chromaDcEnc;
                    HuffmanEncoder acEnc = c == 0 ? lumaAcEnc : chromaAcEnc;
                    ushort[] qt = c == 0 ? lumaQ : chromaQ;
                    dcPredictors[c] = BlockEncoder.EncodeBlock(writer, block, qt, dcEnc, acEnc, dcPredictors[c]);
                }
            }
        }

        writer.Flush();
        MarkerWriter.WriteMarker(output, JpegMarker.Eoi);

        return output.ToArray();
    }

    private static void LoadBlockFromInterleaved(
        byte[] data, int width, int height, int nc,
        int component, int x0, int y0, Span<short> block)
    {
        for (var y = 0; y < 8; y++)
        {
            int sy = y0 + y;
            int clampedY = sy < height ? sy : height - 1;
            for (var x = 0; x < 8; x++)
            {
                int sx = x0 + x;
                int clampedX = sx < width ? sx : width - 1;
                int idx = (clampedY * width + clampedX) * nc + component;
                // Level-shift to signed range [-128, 127].
                block[y * 8 + x] = (short)(data[idx] - 128);
            }
        }
    }
}
