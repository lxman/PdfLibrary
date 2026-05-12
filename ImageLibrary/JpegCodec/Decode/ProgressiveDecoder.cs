using System;
using JpegCodec.Segments;
using JpegCodec.Stream;

namespace JpegCodec.Decode;

// Progressive (SOF2) decoder, implementing T.81 §G.1. The progressive
// decode model:
//
//   * The encoder writes the image as a sequence of SOS scans, each
//     refining a subset of DCT coefficients. We allocate one short[64]
//     block buffer per (component, block) position and accumulate across
//     scans.
//   * Each scan specifies Ss..Se (spectral selection range), Ah (bit
//     position already filled by prior scan), and Al (bit position
//     being filled by this scan).
//   * Four scan types:
//        DC first   (Ss=0,Se=0,Ah=0): decode DC per block, place at Al
//        DC refine  (Ss=0,Se=0,Ah>0): read 1 bit per block, OR at Al
//        AC first   (Ss>0,Ah=0):      decode AC band Ss..Se with EOB run
//        AC refine  (Ss>0,Ah>0):      refine existing non-zero AC and
//                                     fill new ones, with EOB run.
//   * After all scans complete, dequantize+un-zigzag+IDCT+level-shift
//     each block into a component raster.
internal sealed class ProgressiveDecoder
{
    private readonly FrameHeader _frame;
    private readonly int _maxH;
    private readonly int _maxV;
    private readonly int _mcusPerLine;
    private readonly int _mcusPerColumn;
    private readonly short[][] _coefficients; // [componentIdx][blockIdx*64+k] (zigzag order)
    private readonly int[] _componentBlockWidth;   // mcusPerLine * H(c)
    private readonly int[] _componentBlockHeight;  // mcusPerColumn * V(c)
    private int _eobrun;

    public ProgressiveDecoder(FrameHeader frame)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));

        for (var i = 0; i < frame.Components.Length; i++)
        {
            if (frame.Components[i].HorizontalSampling > _maxH) _maxH = frame.Components[i].HorizontalSampling;
            if (frame.Components[i].VerticalSampling > _maxV) _maxV = frame.Components[i].VerticalSampling;
        }

        int mcuWidthPixels = 8 * _maxH;
        int mcuHeightPixels = 8 * _maxV;
        _mcusPerLine = (frame.Width + mcuWidthPixels - 1) / mcuWidthPixels;
        _mcusPerColumn = (frame.Height + mcuHeightPixels - 1) / mcuHeightPixels;

        int nf = frame.NumberOfComponents;
        _coefficients = new short[nf][];
        _componentBlockWidth = new int[nf];
        _componentBlockHeight = new int[nf];
        _componentNaturalBlockWidth = new int[nf];
        _componentNaturalBlockHeight = new int[nf];
        for (var c = 0; c < nf; c++)
        {
            int blocksX = _mcusPerLine * frame.Components[c].HorizontalSampling;
            int blocksY = _mcusPerColumn * frame.Components[c].VerticalSampling;
            _componentBlockWidth[c] = blocksX;
            _componentBlockHeight[c] = blocksY;

            // Natural block count — what non-interleaved scans iterate.
            // Per T.81 §A.2.4 the encoder pads each row to a full
            // 8-sample block boundary in a non-interleaved scan, NOT
            // to the MCU boundary. (Interleaved scans pad to MCU.)
            int hC = frame.Components[c].HorizontalSampling;
            int vC = frame.Components[c].VerticalSampling;
            int compPixelW = (frame.Width * hC + _maxH - 1) / _maxH;
            int compPixelH = (frame.Height * vC + _maxV - 1) / _maxV;
            _componentNaturalBlockWidth[c] = (compPixelW + 7) / 8;
            _componentNaturalBlockHeight[c] = (compPixelH + 7) / 8;

            _coefficients[c] = new short[blocksX * blocksY * 64];
        }
    }

    private readonly int[] _componentNaturalBlockWidth;
    private readonly int[] _componentNaturalBlockHeight;

    public int McusPerLine => _mcusPerLine;
    public int McusPerColumn => _mcusPerColumn;
    public int MaxHorizontalSampling => _maxH;
    public int MaxVerticalSampling => _maxV;

    public void DecodeScan(
        JpegByteSource byteSource,
        JpegBitReader bitReader,
        ScanHeader scan,
        HuffmanCanonicalTable?[] dcTables,
        HuffmanCanonicalTable?[] acTables,
        int restartInterval)
    {
        ScanComponentInfo[] scanInfo = BuildScanInfo(scan, dcTables, acTables);
        var dcPredictors = new int[scan.NumberOfComponents];
        _eobrun = 0;
        var mcuCounter = 0;
        var expectedRst = 0;

        bool isDcScan = scan.SpectralStart == 0;
        bool isInterleaved = scan.NumberOfComponents > 1;
        if (isInterleaved && !isDcScan)
            throw new InvalidOperationException(
                "Progressive AC scan with multiple components is not legal per T.81 §G.1.1.1.1.");

        int mcusX, mcusY;
        if (isInterleaved)
        {
            mcusX = _mcusPerLine;
            mcusY = _mcusPerColumn;
        }
        else
        {
            // T.81 §A.2.4 — non-interleaved scan iterates the NATURAL
            // block grid (rounded to 8-sample boundaries), NOT the
            // MCU-aligned grid. For a Y component (H=V=2) at width 341,
            // that's 43 blocks per row, not 44 — the MCU's padding
            // column is only present in interleaved layout.
            int frameIdx = scanInfo[0].FrameComponentIndex;
            mcusX = _componentNaturalBlockWidth[frameIdx];
            mcusY = _componentNaturalBlockHeight[frameIdx];
        }

        for (var my = 0; my < mcusY; my++)
        {
            for (var mx = 0; mx < mcusX; mx++)
            {
                if (isInterleaved)
                {
                    // Interleaved DC scan (only DC scans may be interleaved
                    // per T.81 §G.1.1.1.1). Dispatch DcFirst vs DcRefine
                    // by Ah.
                    bool refine = scan.ApproxHigh != 0;
                    for (var s = 0; s < scan.NumberOfComponents; s++)
                    {
                        ScanComponentInfo info = scanInfo[s];
                        for (var by = 0; by < info.VerticalSampling; by++)
                        {
                            for (var bx = 0; bx < info.HorizontalSampling; bx++)
                            {
                                int blockX = mx * info.HorizontalSampling + bx;
                                int blockY = my * info.VerticalSampling + by;
                                Span<short> blk = GetBlock(info.FrameComponentIndex, blockX, blockY);
                                if (refine)
                                    DecodeDcRefine(bitReader, blk, scan.ApproxLow);
                                else
                                    DecodeDcFirst(bitReader, info, ref dcPredictors[s], blk, scan.ApproxLow);
                            }
                        }
                    }
                }
                else
                {
                    ScanComponentInfo info = scanInfo[0];
                    Span<short> block = GetBlock(info.FrameComponentIndex, mx, my);

                    if (isDcScan)
                    {
                        if (scan.ApproxHigh == 0)
                            DecodeDcFirst(bitReader, info, ref dcPredictors[0], block, scan.ApproxLow);
                        else
                            DecodeDcRefine(bitReader, block, scan.ApproxLow);
                    }
                    else
                    {
                        if (scan.ApproxHigh == 0)
                            DecodeAcFirst(bitReader, info, block, scan.SpectralStart, scan.SpectralEnd, scan.ApproxLow);
                        else
                            DecodeAcRefine(bitReader, info, block, scan.SpectralStart, scan.SpectralEnd, scan.ApproxLow);
                    }
                }

                if (restartInterval > 0)
                {
                    mcuCounter++;
                    if (mcuCounter == restartInterval &&
                        !(mx == mcusX - 1 && my == mcusY - 1))
                    {
                        HandleRestart(byteSource, bitReader, expectedRst);
                        expectedRst = (expectedRst + 1) & 7;
                        mcuCounter = 0;
                        for (var s = 0; s < dcPredictors.Length; s++) dcPredictors[s] = 0;
                        _eobrun = 0;
                    }
                }
            }
        }
    }

    private Span<short> GetBlock(int componentIndex, int blockX, int blockY)
    {
        int rowStride = _componentBlockWidth[componentIndex];
        int blockOff = (blockY * rowStride + blockX) * 64;
        return _coefficients[componentIndex].AsSpan(blockOff, 64);
    }

    // T.81 §G.1.2.1
    private static void DecodeDcFirst(
        JpegBitReader reader,
        in ScanComponentInfo info,
        ref int dcPredictor,
        Span<short> block,
        int al)
    {
        if (info.DcTable is null)
            throw new InvalidOperationException("DC first scan called with null DC table.");
        int ssss = HuffmanDecoder.DecodeSymbol(reader, info.DcTable);
        int diff = ssss == 0 ? 0 : reader.Receive(ssss);
        dcPredictor += diff;
        block[0] = (short)(dcPredictor << al);
    }

    // T.81 §G.1.2.2
    private static void DecodeDcRefine(JpegBitReader reader, Span<short> block, int al)
    {
        if (reader.ReadBit() == 1)
            block[0] = (short)(block[0] | (1 << al));
    }

    // T.81 §G.1.2.3 — AC first-scan: decode AC band Ss..Se using EOB run.
    private void DecodeAcFirst(
        JpegBitReader reader,
        in ScanComponentInfo info,
        Span<short> block,
        int ss,
        int se,
        int al)
    {
        if (_eobrun > 0)
        {
            _eobrun--;
            return;
        }

        int k = ss;
        while (k <= se)
        {
            int rs = HuffmanDecoder.DecodeSymbol(reader, info.AcTable);
            int r = rs >> 4;
            int s = rs & 0xF;
            if (s == 0)
            {
                if (r == 15)
                {
                    k += 16;
                    continue;
                }
                // EOBn — EOB run of 2^r + bits(r) blocks.
                _eobrun = (1 << r) - 1;
                if (r > 0) _eobrun += reader.ReadBits(r);
                break;
            }
            k += r;
            if (k > se)
                throw new InvalidOperationException(
                    $"AC first-scan run overflows spectral end at k={k} (Se={se}).");
            int value = reader.Receive(s);
            block[k] = (short)(value << al);
            k++;
        }
    }

    // T.81 §G.1.2.4 — AC refinement scan. Structured to match
    // JpegLibrary's ReadBlockProgressiveACRefined verbatim:
    // outer for(; k <= end; k++) walks the band, breaks on EOBn,
    // then a separate `if (_eobrun > 0)` tail block applies refinement
    // bits to any non-zero coefficients remaining in the band.
    private void DecodeAcRefine(
        JpegBitReader reader,
        in ScanComponentInfo info,
        Span<short> block,
        int ss,
        int se,
        int al)
    {
        int p1 = 1 << al;
        int m1 = -1 << al;
        int k = ss;

        if (_eobrun == 0)
        {
            for (; k <= se; k++)
            {
                int rs = HuffmanDecoder.DecodeSymbol(reader, info.AcTable);
                int r = rs >> 4;
                int s = rs & 0xF;

                if (s != 0)
                {
                    // Per T.81 §G.1.2.4 the size for new coefficients in
                    // refinement scans is always 1 (one sign bit).
                    s = reader.ReadBit() != 0 ? p1 : m1;
                }
                else
                {
                    if (r != 15)
                    {
                        _eobrun = 1 << r;
                        if (r != 0) _eobrun += reader.ReadBits(r);
                        break;
                    }
                    // ZRL: r==15, s==0. Walk advances by 16 (zero slots).
                }

                // Inner walk: refine non-zero coefficients along the way,
                // count down zero slots until we've skipped r of them.
                // Note the matching of JpegLibrary's `do { ... } while
                // (k <= end);` — k advances inside the loop, exits when
                // either r underflows or we run off the band.
                while (true)
                {
                    short coef = block[k];
                    if (coef != 0)
                    {
                        if (reader.ReadBit() != 0)
                        {
                            if ((coef & p1) == 0)
                                block[k] = (short)(coef + (coef >= 0 ? p1 : m1));
                        }
                    }
                    else
                    {
                        if (--r < 0) break;
                    }
                    k++;
                    if (k > se) break;
                }

                if (s != 0 && k <= se)
                {
                    block[k] = (short)s;
                }
                // The outer `for`'s k++ advances past this position.
            }
        }

        if (_eobrun > 0)
        {
            for (; k <= se; k++)
            {
                short coef = block[k];
                if (coef != 0)
                {
                    if (reader.ReadBit() != 0)
                    {
                        if ((coef & p1) == 0)
                            block[k] = (short)(coef + (coef >= 0 ? p1 : m1));
                    }
                }
            }
            _eobrun--;
        }
    }

    // Kept for reference; no longer called now that DecodeAcRefine inlines
    // the structure that JpegLibrary uses.
    private static void ApplyAcRefinementBits(
        JpegBitReader reader, Span<short> block, int ss, int se, int p1, int m1)
    {
        for (int k = ss; k <= se; k++)
        {
            short cur = block[k];
            if (cur != 0)
            {
                if (reader.ReadBit() == 1)
                {
                    if ((cur & p1) == 0)
                        block[k] = (short)(cur + (cur >= 0 ? p1 : m1));
                }
            }
        }
    }

    private void HandleRestart(JpegByteSource byteSource, JpegBitReader bitReader, int expectedRst)
    {
        // The bit reader's accumulator holds the encoder's pad bits (T.81
        // §F.1.2.3 — 1-bit fill after the last data bit). Discard them and
        // force the byte source to find the next marker.
        bitReader.ResetForNewSegment();
        if (!byteSource.AtMarker)
            byteSource.ReadByte();
        if (!byteSource.AtMarker)
            throw new InvalidOperationException("Expected RST marker after restart interval.");
        var expected = (JpegMarker)((byte)JpegMarker.Rst0 + expectedRst);
        if (byteSource.EncounteredMarker != expected)
            throw new InvalidOperationException(
                $"Restart marker mismatch: expected {expected}, got {byteSource.EncounteredMarker}.");
        byteSource.ConsumeMarker();
    }

    // After all scans, dequantize+un-zigzag+IDCT+level-shift each block.
    public byte[][] FinalizeRasters(ushort[]?[] quantTables)
    {
        int nf = _frame.NumberOfComponents;
        var rasters = new byte[nf][];
        for (var c = 0; c < nf; c++)
        {
            int rasterW = _componentBlockWidth[c] * 8;
            int rasterH = _componentBlockHeight[c] * 8;
            var raster = new byte[rasterW * rasterH];
            ushort[]? quant = quantTables[_frame.Components[c].QuantizationTableId];
            if (quant is null)
                throw new InvalidOperationException(
                    $"Component {c} references missing quantization table {_frame.Components[c].QuantizationTableId}.");

            int blockW = _componentBlockWidth[c];
            int blockH = _componentBlockHeight[c];
            short[] coefs = _coefficients[c];
            // stackalloc lifetime is per METHOD, not per scope — so the
            // buffers go OUTSIDE the loop.
            Span<short> natural = stackalloc short[64];
            Span<short> idctOut = stackalloc short[64];
            for (var by = 0; by < blockH; by++)
            {
                for (var bx = 0; bx < blockW; bx++)
                {
                    int blockOff = (by * blockW + bx) * 64;
                    natural.Clear();
                    for (var k = 0; k < 64; k++)
                        natural[ZigZag.ZigzagToNatural[k]] = (short)(coefs[blockOff + k] * quant[k]);
                    InverseDct.Apply(natural, idctOut);

                    int dstY0 = by * 8;
                    int dstX0 = bx * 8;
                    for (var y = 0; y < 8; y++)
                    {
                        int rowOff = (dstY0 + y) * rasterW + dstX0;
                        int blkRow = y * 8;
                        for (var x = 0; x < 8; x++)
                            raster[rowOff + x] = LevelShift.Shift(idctOut[blkRow + x]);
                    }
                }
            }
            rasters[c] = raster;
        }
        return rasters;
    }

    private ScanComponentInfo[] BuildScanInfo(
        ScanHeader scan,
        HuffmanCanonicalTable?[] dcTables,
        HuffmanCanonicalTable?[] acTables)
    {
        var infos = new ScanComponentInfo[scan.NumberOfComponents];
        for (var s = 0; s < scan.NumberOfComponents; s++)
        {
            ScanComponent sc = scan.Components[s];
            int frameIdx = FindFrameComponentIndex(sc.ComponentSelector);
            FrameComponent fc = _frame.Components[frameIdx];
            infos[s] = new ScanComponentInfo
            {
                FrameComponentIndex = frameIdx,
                HorizontalSampling = fc.HorizontalSampling,
                VerticalSampling = fc.VerticalSampling,
                DcTable = scan.SpectralStart == 0 && sc.DcTableId < dcTables.Length ? dcTables[sc.DcTableId] : null,
                AcTable = scan.SpectralEnd > 0 && sc.AcTableId < acTables.Length ? acTables[sc.AcTableId] : null,
            };
        }
        return infos;
    }

    private int FindFrameComponentIndex(byte componentId)
    {
        for (var i = 0; i < _frame.Components.Length; i++)
            if (_frame.Components[i].Identifier == componentId) return i;
        throw new InvalidOperationException(
            $"Scan references component {componentId} not in frame.");
    }

    private struct ScanComponentInfo
    {
        public int FrameComponentIndex;
        public byte HorizontalSampling;
        public byte VerticalSampling;
        public HuffmanCanonicalTable? DcTable;
        public HuffmanCanonicalTable? AcTable;
    }
}
