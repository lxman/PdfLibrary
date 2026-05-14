using System;
using JpegCodec.Segments;
using JpegCodec.Stream;

namespace JpegCodec.Decode;

// Sequential MCU walker for SOF0 / SOF1 (baseline + extended sequential
// Huffman). Implements T.81 §F.1.2 and §F.2.
//
// Owns per-scan state: DC predictors, restart MCU counter, expected RST
// index. Produces a per-component "block raster" buffer (level-shifted
// 8-bit samples in component-native resolution); upsampling to image
// resolution is a separate step.
internal sealed class McuWalker
{
    private readonly FrameHeader _frame;
    private readonly ScanHeader _scan;
    private readonly ushort[]?[] _quantTables;
    private readonly HuffmanCanonicalTable?[] _dcTables;
    private readonly HuffmanCanonicalTable?[] _acTables;
    private readonly int _restartInterval;
    private readonly int _maxH;
    private readonly int _maxV;
    private readonly int _mcusPerLine;
    private readonly int _mcusPerColumn;
    private readonly int[] _dcPredictors;

    // Component metadata in scan order — keyed by index into ScanHeader.Components.
    private readonly ScanComponentDecodeInfo[] _scanInfo;

    private readonly bool _isInterleaved;
    private readonly int _nonInterleavedBlockWidth;
    private readonly int _nonInterleavedBlockHeight;

    public McuWalker(
        FrameHeader frame,
        ScanHeader scan,
        ushort[]?[] quantTables,
        HuffmanCanonicalTable?[] dcTables,
        HuffmanCanonicalTable?[] acTables,
        int restartInterval)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _scan = scan ?? throw new ArgumentNullException(nameof(scan));
        _quantTables = quantTables ?? throw new ArgumentNullException(nameof(quantTables));
        _dcTables = dcTables ?? throw new ArgumentNullException(nameof(dcTables));
        _acTables = acTables ?? throw new ArgumentNullException(nameof(acTables));
        _restartInterval = restartInterval;

        _maxH = 0;
        _maxV = 0;
        for (var i = 0; i < frame.Components.Length; i++)
        {
            if (frame.Components[i].HorizontalSampling > _maxH) _maxH = frame.Components[i].HorizontalSampling;
            if (frame.Components[i].VerticalSampling > _maxV) _maxV = frame.Components[i].VerticalSampling;
        }
        if (_maxH == 0 || _maxV == 0)
            throw new InvalidOperationException("Frame has zero sampling factor.");

        int mcuWidthPixels = 8 * _maxH;
        int mcuHeightPixels = 8 * _maxV;
        _mcusPerLine = (frame.Width + mcuWidthPixels - 1) / mcuWidthPixels;
        _mcusPerColumn = (frame.Height + mcuHeightPixels - 1) / mcuHeightPixels;

        // T.81 §A.2.4 — interleaved scans iterate MCUs (with H*V blocks
        // per component per MCU). Non-interleaved scans (Ns == 1) iterate
        // the single component's natural block grid, which for an
        // image that isn't a multiple of (8 * maxH/Hc) samples wide can
        // be 1 column smaller than the MCU-aligned grid would suggest.
        _isInterleaved = scan.NumberOfComponents > 1;
        if (!_isInterleaved)
        {
            int frameIdx = FindFrameComponentIndex(scan.Components[0].ComponentSelector);
            int hC = frame.Components[frameIdx].HorizontalSampling;
            int vC = frame.Components[frameIdx].VerticalSampling;
            int compPixelW = (frame.Width * hC + _maxH - 1) / _maxH;
            int compPixelH = (frame.Height * vC + _maxV - 1) / _maxV;
            _nonInterleavedBlockWidth = (compPixelW + 7) / 8;
            _nonInterleavedBlockHeight = (compPixelH + 7) / 8;
        }

        _scanInfo = new ScanComponentDecodeInfo[scan.NumberOfComponents];
        for (var s = 0; s < scan.NumberOfComponents; s++)
        {
            ScanComponent sc = scan.Components[s];
            int frameIndex = FindFrameComponentIndex(sc.ComponentSelector);
            FrameComponent fc = frame.Components[frameIndex];
            _scanInfo[s] = new ScanComponentDecodeInfo
            {
                FrameComponentIndex = frameIndex,
                HorizontalSampling = fc.HorizontalSampling,
                VerticalSampling = fc.VerticalSampling,
                DcTable = _dcTables[sc.DcTableId] ?? throw new InvalidOperationException(
                    $"Scan references missing DC Huffman table {sc.DcTableId}."),
                AcTable = _acTables[sc.AcTableId] ?? throw new InvalidOperationException(
                    $"Scan references missing AC Huffman table {sc.AcTableId}."),
                QuantTable = _quantTables[fc.QuantizationTableId] ?? throw new InvalidOperationException(
                    $"Scan references missing quantization table {fc.QuantizationTableId}."),
            };
        }

        _dcPredictors = new int[scan.NumberOfComponents];
    }

    public int McusPerLine => _mcusPerLine;
    public int McusPerColumn => _mcusPerColumn;
    public int MaxHorizontalSampling => _maxH;
    public int MaxVerticalSampling => _maxV;

    // Decode all MCUs from byteSource. componentRasters[c] is a flat byte
    // array sized componentRasterWidth(c) * componentRasterHeight(c) where
    //   componentRasterWidth(c)  = _mcusPerLine   * 8 * H(c)
    //   componentRasterHeight(c) = _mcusPerColumn * 8 * V(c)
    // Samples are post-level-shift unsigned bytes [0..255].
    public void Decode(JpegByteSource byteSource, JpegBitReader bitReader, byte[][] componentRasters)
    {
        Array.Clear(_dcPredictors, 0, _dcPredictors.Length);
        var mcuCounter = 0;
        var expectedRst = 0;

        int mcusX, mcusY;
        if (_isInterleaved)
        {
            mcusX = _mcusPerLine;
            mcusY = _mcusPerColumn;
        }
        else
        {
            mcusX = _nonInterleavedBlockWidth;
            mcusY = _nonInterleavedBlockHeight;
        }

        for (var my = 0; my < mcusY; my++)
        {
            for (var mx = 0; mx < mcusX; mx++)
            {
                if (_isInterleaved)
                    DecodeMcu(bitReader, componentRasters, mx, my);
                else
                    DecodeNonInterleavedBlock(bitReader, componentRasters, mx, my);

                if (_restartInterval > 0)
                {
                    mcuCounter++;
                    if (mcuCounter == _restartInterval &&
                        !(mx == mcusX - 1 && my == mcusY - 1))
                    {
                        HandleRestart(byteSource, bitReader, expectedRst);
                        expectedRst = (expectedRst + 1) & 0x07;
                        mcuCounter = 0;
                        Array.Clear(_dcPredictors, 0, _dcPredictors.Length);
                    }
                }
            }
        }
    }

    // Non-interleaved scan: each "MCU" is a single block of the single
    // scan component, addressed at the component's natural block grid.
    private void DecodeNonInterleavedBlock(JpegBitReader bitReader, byte[][] componentRasters, int blockX, int blockY)
    {
        ScanComponentDecodeInfo info = _scanInfo[0];
        byte[] raster = componentRasters[info.FrameComponentIndex];
        int rasterWidth = _mcusPerLine * 8 * info.HorizontalSampling;
        DecodeBlock(bitReader, info, ref _dcPredictors[0], raster, rasterWidth, blockX, blockY);
    }

    private void HandleRestart(JpegByteSource byteSource, JpegBitReader bitReader, int expectedRst)
    {
        // The bit reader's accumulator holds the encoder's pad bits
        // (T.81 §F.1.2.3). Discard them and force the byte source to
        // find the next marker.
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

    private void DecodeMcu(JpegBitReader bitReader, byte[][] componentRasters, int mx, int my)
    {
        for (var s = 0; s < _scan.NumberOfComponents; s++)
        {
            ScanComponentDecodeInfo info = _scanInfo[s];
            byte[] raster = componentRasters[info.FrameComponentIndex];
            int rasterWidth = _mcusPerLine * 8 * info.HorizontalSampling;

            for (var by = 0; by < info.VerticalSampling; by++)
            {
                for (var bx = 0; bx < info.HorizontalSampling; bx++)
                {
                    int blockX = mx * info.HorizontalSampling + bx;
                    int blockY = my * info.VerticalSampling + by;
                    DecodeBlock(bitReader, info, ref _dcPredictors[s], raster, rasterWidth, blockX, blockY);
                }
            }
        }
    }

    private void DecodeBlock(
        JpegBitReader bitReader,
        ScanComponentDecodeInfo info,
        ref int dcPredictor,
        byte[] raster,
        int rasterWidth,
        int blockX,
        int blockY)
    {
        Span<short> natural = stackalloc short[64];

        int ssss = HuffmanDecoder.DecodeSymbol(bitReader, info.DcTable);
        if (ssss > 0)
            dcPredictor += bitReader.Receive(ssss);
        natural[0] = (short)(dcPredictor * info.QuantTable[0]);

        var k = 1;
        while (k < 64)
        {
            int rs = HuffmanDecoder.DecodeSymbol(bitReader, info.AcTable);
            int run = (rs >> 4) & 0xF;
            int size = rs & 0xF;

            if (size == 0)
            {
                if (run == 0xF)
                {
                    k += 16;
                    continue;
                }
                break;
            }

            k += run;
            if (k >= 64)
                throw new InvalidOperationException(
                    $"AC run overflow at position {k} (run={run}, size={size}).");
            int value = bitReader.Receive(size);
            natural[ZigZag.ZigzagToNatural[k]] = (short)(value * info.QuantTable[k]);
            k++;
        }

        int rasterOffset = blockY * 8 * rasterWidth + blockX * 8;
        InverseDct.ApplyAndShiftToBytes(natural, raster, rasterOffset, rasterWidth);
    }

    private int FindFrameComponentIndex(byte componentId)
    {
        for (var i = 0; i < _frame.Components.Length; i++)
            if (_frame.Components[i].Identifier == componentId) return i;
        throw new InvalidOperationException(
            $"Scan references component {componentId} not in frame.");
    }

    private struct ScanComponentDecodeInfo
    {
        public int FrameComponentIndex;
        public byte HorizontalSampling;
        public byte VerticalSampling;
        public HuffmanCanonicalTable DcTable;
        public HuffmanCanonicalTable AcTable;
        public ushort[] QuantTable;
    }
}
