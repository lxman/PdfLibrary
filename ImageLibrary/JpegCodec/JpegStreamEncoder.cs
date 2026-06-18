using System;
using System.Collections.Generic;
using ImageResampling;
using JpegCodec.Encode;
using JpegCodec.Internal;
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

        bool use420 = options.ChromaSubsampling == ChromaSubsampling.Yuv420 && nc == 3;

        return use420
            ? Encode420(componentData, options)
            : Encode444(componentData, options);
    }

    // -----------------------------------------------------------------------
    // 4:4:4 path — original behaviour, all components H=V=1
    // -----------------------------------------------------------------------
    private static byte[] Encode444(byte[] componentData, JpegEncodeOptions options)
    {
        int width = options.Width;
        int height = options.Height;
        int nc = options.NumberOfComponents;

        const int H = 1, V = 1;

        ushort[] lumaQ = StandardJpegTables.ScaleQuantTable(StandardJpegTables.LumaQuantBase50, options.Quality);
        ushort[] chromaQ = nc == 1
            ? lumaQ
            : StandardJpegTables.ScaleQuantTable(StandardJpegTables.ChromaQuantBase50, options.Quality);

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

        var output = new List<byte>(componentData.Length / 4);

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

        var writer = new BitWriter(output);
        var dcPredictors = new int[nc];

        int mcuW = 8 * H, mcuH = 8 * V;
        int mcusPerLine = (width + mcuW - 1) / mcuW;
        int mcusPerCol  = (height + mcuH - 1) / mcuH;
        for (var my = 0; my < mcusPerCol; my++)
        {
            for (var mx = 0; mx < mcusPerLine; mx++)
            {
                for (var c = 0; c < nc; c++)
                {
                    Span<short> block = stackalloc short[64];
                    LoadBlockFromInterleaved(componentData, width, height, nc, c, mx * 8, my * 8, block);

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

    // -----------------------------------------------------------------------
    // 4:2:0 path — luma H=2/V=2, chroma H=1/V=1
    // -----------------------------------------------------------------------
    private static byte[] Encode420(byte[] componentData, JpegEncodeOptions options)
    {
        int width  = options.Width;
        int height = options.Height;

        // RGB → separate Y, Cb, Cr planes (full resolution).
        YCbCrConverter.RgbToYCbCrPlanar(componentData, width, height,
            out byte[] yPlane, out byte[] cbPlane, out byte[] crPlane);

        // Downsample chroma planes 2:1 in both axes using the box-filter resampler.
        int chromaW = (width  + 1) >> 1;   // ceil(width/2)
        int chromaH = (height + 1) >> 1;   // ceil(height/2)
        byte[] cbDown = ImageResampler.Resample(cbPlane, width, height, 1, chromaW, chromaH);
        byte[] crDown = ImageResampler.Resample(crPlane, width, height, 1, chromaW, chromaH);

        // Quantization tables.
        ushort[] lumaQ   = StandardJpegTables.ScaleQuantTable(StandardJpegTables.LumaQuantBase50,   options.Quality);
        ushort[] chromaQ = StandardJpegTables.ScaleQuantTable(StandardJpegTables.ChromaQuantBase50, options.Quality);

        // Huffman tables.
        var lumaDcCanonical   = HuffmanCanonicalTable.Build(StandardJpegTables.LumaDcBits,   StandardJpegTables.LumaDcValues);
        var lumaAcCanonical   = HuffmanCanonicalTable.Build(StandardJpegTables.LumaAcBits,   StandardJpegTables.LumaAcValues);
        var chromaDcCanonical = HuffmanCanonicalTable.Build(StandardJpegTables.ChromaDcBits, StandardJpegTables.ChromaDcValues);
        var chromaAcCanonical = HuffmanCanonicalTable.Build(StandardJpegTables.ChromaAcBits, StandardJpegTables.ChromaAcValues);

        var lumaDcEnc   = new HuffmanEncoder(lumaDcCanonical);
        var lumaAcEnc   = new HuffmanEncoder(lumaAcCanonical);
        var chromaDcEnc = new HuffmanEncoder(chromaDcCanonical);
        var chromaAcEnc = new HuffmanEncoder(chromaAcCanonical);

        var output = new List<byte>(width * height * 3 / 4);

        MarkerWriter.WriteMarker(output, JpegMarker.Soi);

        if (options.EmitJfif)
            MarkerWriter.WriteJfif(output);

        if (options.EmitAdobeMarker)
            MarkerWriter.WriteAdobeApp14(output, options.AdobeColorTransform);

        MarkerWriter.WriteDqt(output, 0, lumaQ);
        MarkerWriter.WriteDqt(output, 1, chromaQ);

        // SOF0: luma (comp 1) H=2/V=2, chroma Cb (2) and Cr (3) H=1/V=1.
        var frameComponents = new[]
        {
            new MarkerWriter.FrameComponentSpec(id: 1, hSampling: 2, vSampling: 2, quantTableId: 0),
            new MarkerWriter.FrameComponentSpec(id: 2, hSampling: 1, vSampling: 1, quantTableId: 1),
            new MarkerWriter.FrameComponentSpec(id: 3, hSampling: 1, vSampling: 1, quantTableId: 1),
        };
        MarkerWriter.WriteSof0(output, width, height, precision: 8, frameComponents);

        MarkerWriter.WriteDht(output, tableClass: 0, tableId: 0,
            StandardJpegTables.LumaDcBits, StandardJpegTables.LumaDcValues);
        MarkerWriter.WriteDht(output, tableClass: 1, tableId: 0,
            StandardJpegTables.LumaAcBits, StandardJpegTables.LumaAcValues);
        MarkerWriter.WriteDht(output, tableClass: 0, tableId: 1,
            StandardJpegTables.ChromaDcBits, StandardJpegTables.ChromaDcValues);
        MarkerWriter.WriteDht(output, tableClass: 1, tableId: 1,
            StandardJpegTables.ChromaAcBits, StandardJpegTables.ChromaAcValues);

        var scanComponents = new[]
        {
            new MarkerWriter.ScanComponentSpec(id: 1, dcTableId: 0, acTableId: 0),
            new MarkerWriter.ScanComponentSpec(id: 2, dcTableId: 1, acTableId: 1),
            new MarkerWriter.ScanComponentSpec(id: 3, dcTableId: 1, acTableId: 1),
        };
        MarkerWriter.WriteSos(output, scanComponents, ss: 0, se: 63, ah: 0, al: 0);

        var writer = new BitWriter(output);

        // For 4:2:0 (maxH=2, maxV=2):
        //   MCU size = 16×16 pixels.
        //   Each MCU contains: 4 Y-blocks (2×2), 1 Cb-block, 1 Cr-block.
        int mcusPerLine = (width  + 15) / 16;
        int mcusPerCol  = (height + 15) / 16;

        int dcY = 0, dcCb = 0, dcCr = 0;

        for (var my = 0; my < mcusPerCol; my++)
        {
            for (var mx = 0; mx < mcusPerLine; mx++)
            {
                // 4 luma blocks, order: (0,0), (1,0), (0,1), (1,1)
                // bx/by are sub-block offsets within the MCU (0 or 1).
                for (var by = 0; by < 2; by++)
                {
                    for (var bx = 0; bx < 2; bx++)
                    {
                        Span<short> block = stackalloc short[64];
                        LoadBlockFromPlanar(yPlane, width, height, mx * 16 + bx * 8, my * 16 + by * 8, block);
                        dcY = BlockEncoder.EncodeBlock(writer, block, lumaQ, lumaDcEnc, lumaAcEnc, dcY);
                    }
                }

                // 1 Cb block (on the downsampled chroma plane).
                {
                    Span<short> block = stackalloc short[64];
                    LoadBlockFromPlanar(cbDown, chromaW, chromaH, mx * 8, my * 8, block);
                    dcCb = BlockEncoder.EncodeBlock(writer, block, chromaQ, chromaDcEnc, chromaAcEnc, dcCb);
                }

                // 1 Cr block.
                {
                    Span<short> block = stackalloc short[64];
                    LoadBlockFromPlanar(crDown, chromaW, chromaH, mx * 8, my * 8, block);
                    dcCr = BlockEncoder.EncodeBlock(writer, block, chromaQ, chromaDcEnc, chromaAcEnc, dcCr);
                }
            }
        }

        writer.Flush();
        MarkerWriter.WriteMarker(output, JpegMarker.Eoi);

        return output.ToArray();
    }

    // -----------------------------------------------------------------------
    // Block loaders
    // -----------------------------------------------------------------------

    /// <summary>
    /// Load an 8×8 block from an interleaved (multi-component) pixel buffer.
    /// </summary>
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
                block[y * 8 + x] = (short)(data[idx] - 128);
            }
        }
    }

    /// <summary>
    /// Load an 8×8 block from a single-channel planar pixel buffer.
    /// Edge pixels are replicated when the block extends past the buffer boundary.
    /// </summary>
    private static void LoadBlockFromPlanar(
        byte[] plane, int planeWidth, int planeHeight,
        int x0, int y0, Span<short> block)
    {
        for (var y = 0; y < 8; y++)
        {
            int sy = y0 + y;
            int clampedY = sy < planeHeight ? sy : planeHeight - 1;
            for (var x = 0; x < 8; x++)
            {
                int sx = x0 + x;
                int clampedX = sx < planeWidth ? sx : planeWidth - 1;
                int idx = clampedY * planeWidth + clampedX;
                block[y * 8 + x] = (short)(plane[idx] - 128);
            }
        }
    }
}
