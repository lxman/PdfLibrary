using System;
using System.Buffers;
using System.IO;
using System.Linq;

namespace Compressors.Jpeg;

/// <summary>
/// Decodes baseline JPEG images.
/// Supports SOF0 (baseline DCT) with Huffman coding.
/// </summary>
public class JpegDecoder
{
    private Stream? _stream;
    private BitReader? _bitReader;

    // Image properties
    private int _width;
    private int _height;
    private int _componentCount;
    private JpegComponent[] _components = Array.Empty<JpegComponent>();

    // Tables
    private readonly int[][] _quantTables = new int[4][];
    private readonly HuffmanTable?[] _dcTables = new HuffmanTable[4];
    private readonly HuffmanTable?[] _acTables = new HuffmanTable[4];

    // Decoding state
    private int _restartInterval;
    private int _mcuWidth;
    private int _mcuHeight;
    private int _mcusPerRow;
    private int _mcuRows;

    // Adobe marker info
    private bool _hasAdobeMarker;
    private byte _adobeColorTransform;

    /// <summary>
    /// Gets the image width.
    /// </summary>
    public int Width => _width;

    /// <summary>
    /// Gets the image height.
    /// </summary>
    public int Height => _height;

    /// <summary>
    /// Gets the number of color components.
    /// </summary>
    public int ComponentCount => _componentCount;

    /// <summary>
    /// Gets whether an Adobe APP14 marker was found.
    /// </summary>
    public bool HasAdobeMarker => _hasAdobeMarker;

    /// <summary>
    /// Gets the Adobe color transform value (0=Unknown/CMYK, 1=YCbCr, 2=YCCK).
    /// </summary>
    public byte AdobeColorTransform => _adobeColorTransform;

    /// <summary>
    /// Decodes a JPEG image from a stream with optional color conversion.
    /// </summary>
    /// <param name="stream">Input stream containing JPEG data</param>
    /// <param name="convertToRgb">If true, converts to RGB. If false, returns raw component data.</param>
    /// <returns>Pixel data (RGB if converted, raw components otherwise)</returns>
    public byte[] Decode(Stream stream, bool convertToRgb = true)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _hasAdobeMarker = false;
        _adobeColorTransform = 0;

        // Read SOI marker
        if (ReadMarker() != JpegConstants.SOI)
            throw new InvalidDataException("Not a valid JPEG file - missing SOI marker");

        // Parse markers until we hit SOS
        while (true)
        {
            byte marker = ReadMarker();

            switch (marker)
            {
                case JpegConstants.SOF0: // Baseline DCT
                    ReadStartOfFrame();
                    break;

                case JpegConstants.SOF1: // Extended sequential DCT
                case JpegConstants.SOF2: // Progressive DCT
                    throw new NotSupportedException($"SOF{marker - 0xC0} not supported - only baseline JPEG (SOF0) is supported");

                case JpegConstants.DHT:
                    ReadHuffmanTable();
                    break;

                case JpegConstants.DQT:
                    ReadQuantizationTable();
                    break;

                case JpegConstants.DRI:
                    ReadRestartInterval();
                    break;

                case JpegConstants.SOS:
                    ReadStartOfScan();
                    return DecodeScan(convertToRgb);

                case JpegConstants.EOI:
                    throw new InvalidDataException("Unexpected EOI marker before image data");

                case 0xEE: // APP14 (Adobe marker)
                    ReadAdobeMarker();
                    break;

                case JpegConstants.APP0:
                case JpegConstants.APP1:
                case JpegConstants.APP2:
                case JpegConstants.COM:
                default:
                    // Skip unknown/unneeded segments
                    SkipSegment();
                    break;
            }
        }
    }

    /// <summary>
    /// Diagnostic method to get raw YCbCr values for a specific pixel.
    /// Must be called after Decode() completes.
    /// </summary>
    public (byte Y, byte Cb, byte Cr) GetYCbCrForPixel(int x, int y)
    {
        if (_componentCount != 3)
            throw new InvalidOperationException("GetYCbCrForPixel requires a 3-component image");

        var yComp = _components[0];
        var cbComp = _components[1];
        var crComp = _components[2];

        int yBlocksPerRow = _mcusPerRow * yComp.BlocksPerMcuH;
        int cbBlocksPerRow = _mcusPerRow * cbComp.BlocksPerMcuH;
        int crBlocksPerRow = _mcusPerRow * crComp.BlocksPerMcuH;

        // Y component
        int yBlockX = x / 8;
        int yBlockY = y / 8;
        int yPixelX = x % 8;
        int yPixelY = y % 8;
        int yOffset = (yBlockY * yBlocksPerRow + yBlockX) * 64 + yPixelY * 8 + yPixelX;
        byte yVal = yComp.Data![yOffset];

        // Cb component
        int cbX = x * cbComp.Width / _width;
        int cbY = y * cbComp.Height / _height;
        int cbBlockX = cbX / 8;
        int cbBlockY = cbY / 8;
        int cbPixelX = cbX % 8;
        int cbPixelY = cbY % 8;
        int cbOffset = (cbBlockY * cbBlocksPerRow + cbBlockX) * 64 + cbPixelY * 8 + cbPixelX;
        byte cbVal = cbComp.Data![cbOffset];

        // Cr component
        int crX = x * crComp.Width / _width;
        int crY = y * crComp.Height / _height;
        int crBlockX = crX / 8;
        int crBlockY = crY / 8;
        int crPixelX = crX % 8;
        int crPixelY = crY % 8;
        int crOffset = (crBlockY * crBlocksPerRow + crBlockX) * 64 + crPixelY * 8 + crPixelX;
        byte crVal = crComp.Data![crOffset];

        return (yVal, cbVal, crVal);
    }

    /// <summary>
    /// Reads Adobe APP14 marker to determine color transform.
    /// </summary>
    private void ReadAdobeMarker()
    {
        int length = ReadUInt16() - 2;
        if (length < 12)
        {
            // Too short, skip it
            if (length > 0)
                _stream!.Seek(length, SeekOrigin.Current);
            return;
        }

        var data = new byte[length];
        ReadExactly(_stream!, data, 0, length);

        // Check for "Adobe" signature
        if (data[0] != 'A' || data[1] != 'd' || data[2] != 'o' ||
            data[3] != 'b' || data[4] != 'e') return;
        _hasAdobeMarker = true;
        // Color transform byte is at offset 11
        if (length > 11)
        {
            _adobeColorTransform = data[11];
        }
    }

    /// <summary>
    /// Helper to read the exact number of bytes (netstandard2.1 compatibility).
    /// </summary>
    private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            int read = stream.Read(buffer, offset + totalRead, count - totalRead);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of stream");
            totalRead += read;
        }
    }

    /// <summary>
    /// Reads a marker from the stream.
    /// </summary>
    private byte ReadMarker()
    {
        int b = _stream!.ReadByte();
        if (b != 0xFF)
            throw new InvalidDataException($"Expected marker prefix 0xFF, got 0x{b:X2}");

        // Skip padding 0xFF bytes
        do
        {
            b = _stream.ReadByte();
        } while (b == 0xFF);

        if (b < 0)
            throw new EndOfStreamException("Unexpected end of stream while reading marker");

        return (byte)b;
    }

    /// <summary>
    /// Reads a 16-bit big-endian value.
    /// </summary>
    private int ReadUInt16()
    {
        int high = _stream!.ReadByte();
        int low = _stream.ReadByte();
        if (high < 0 || low < 0)
            throw new EndOfStreamException("Unexpected end of stream");
        return (high << 8) | low;
    }

    /// <summary>
    /// Skips a segment.
    /// </summary>
    private void SkipSegment()
    {
        int length = ReadUInt16() - 2;
        if (length > 0)
        {
            _stream!.Seek(length, SeekOrigin.Current);
        }
    }

    /// <summary>
    /// Reads the Start of Frame (SOF0) segment.
    /// </summary>
    private void ReadStartOfFrame()
    {
        int length = ReadUInt16();
        int precision = _stream!.ReadByte();

        if (precision != 8)
            throw new NotSupportedException($"Only 8-bit precision is supported, got {precision}");

        _height = ReadUInt16();
        _width = ReadUInt16();
        _componentCount = _stream.ReadByte();

        if (_componentCount < 1 || _componentCount > 4)
            throw new InvalidDataException($"Invalid component count: {_componentCount}");

        _components = new JpegComponent[_componentCount];

        int maxH = 1, maxV = 1;
        for (var i = 0; i < _componentCount; i++)
        {
            int id = _stream.ReadByte();
            int sampling = _stream.ReadByte();
            int quantTableId = _stream.ReadByte();

            int h = (sampling >> 4) & 0x0F;
            int v = sampling & 0x0F;

            _components[i] = new JpegComponent
            {
                Id = id,
                HorizontalSampling = h,
                VerticalSampling = v,
                QuantTableId = quantTableId
            };

            maxH = Math.Max(maxH, h);
            maxV = Math.Max(maxV, v);
        }

        // Calculate MCU dimensions
        _mcuWidth = maxH * 8;
        _mcuHeight = maxV * 8;
        _mcusPerRow = (_width + _mcuWidth - 1) / _mcuWidth;
        _mcuRows = (_height + _mcuHeight - 1) / _mcuHeight;

        // Set component dimensions
        for (var i = 0; i < _componentCount; i++)
        {
            _components[i].Width = (_width * _components[i].HorizontalSampling + maxH - 1) / maxH;
            _components[i].Height = (_height * _components[i].VerticalSampling + maxV - 1) / maxV;
            _components[i].BlocksPerMcuH = _components[i].HorizontalSampling;
            _components[i].BlocksPerMcuV = _components[i].VerticalSampling;
        }
    }

    /// <summary>
    /// Reads Define Huffman Table (DHT) segment.
    /// </summary>
    private void ReadHuffmanTable()
    {
        int length = ReadUInt16() - 2;
        byte[]? data = ArrayPool<byte>.Shared.Rent(length);

        try
        {
            ReadExactly(_stream!, data, 0, length);

            var offset = 0;
            while (offset < length)
            {
                HuffmanTable table = HuffmanTable.FromSegmentData(
                    data.AsSpan(offset),
                    out int tableClass,
                    out int tableId);

                // Calculate size of this table in the data
                int tableSize = 1 + 16;
                for (var i = 0; i < 16; i++)
                    tableSize += table.Bits[i];

                offset += tableSize;

                if (tableClass == 0)
                    _dcTables[tableId] = table;
                else
                    _acTables[tableId] = table;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(data);
        }
    }

    /// <summary>
    /// Reads Define Quantization Table (DQT) segment.
    /// </summary>
    private void ReadQuantizationTable()
    {
        int length = ReadUInt16() - 2;

        while (length > 0)
        {
            int info = _stream!.ReadByte();
            int precision = (info >> 4) & 0x0F;
            int tableId = info & 0x0F;

            if (precision == 0)
            {
                // 8-bit values
                var table = new byte[64];
                ReadExactly(_stream, table, 0, 64);
                _quantTables[tableId] = Quantization.TableFromZigzag(table);
                length -= 65;
            }
            else
            {
                // 16-bit values
                var table = new int[64];
                var zigzag = new int[64];
                for (var i = 0; i < 64; i++)
                {
                    zigzag[i] = ReadUInt16();
                }
                Quantization.FromZigzag(zigzag, table);
                _quantTables[tableId] = table;
                length -= 129;
            }
        }
    }

    /// <summary>
    /// Reads Define Restart Interval (DRI) segment.
    /// </summary>
    private void ReadRestartInterval()
    {
        ReadUInt16(); // length
        _restartInterval = ReadUInt16();
        Console.WriteLine($"DRI: Restart interval = {_restartInterval} MCUs");
    }

    /// <summary>
    /// Reads Start of Scan (SOS) segment.
    /// </summary>
    private void ReadStartOfScan()
    {
        int length = ReadUInt16();
        int componentCount = _stream!.ReadByte();

        for (var i = 0; i < componentCount; i++)
        {
            int componentId = _stream.ReadByte();
            int tableIds = _stream.ReadByte();

            // Find component by ID
            for (var j = 0; j < _componentCount; j++)
            {
                if (_components[j].Id != componentId) continue;
                _components[j].DcTableId = (tableIds >> 4) & 0x0F;
                _components[j].AcTableId = tableIds & 0x0F;
                break;
            }
        }

        // Skip spectral selection and approximation (baseline ignores these)
        _stream.ReadByte(); // Ss
        _stream.ReadByte(); // Se
        _stream.ReadByte(); // Ah, Al
    }

    /// <summary>
    /// Decodes the image scan data.
    /// </summary>
    private byte[] DecodeScan(bool convertToRgb)
    {
        Console.WriteLine($"\n=== Starting DecodeScan ===");
        Console.WriteLine($"Stream position before BitReader: {_stream!.Position}");
        Console.WriteLine($"Image dimensions: {_width}×{_height}");
        Console.WriteLine($"MCU dimensions: {_mcuWidth}×{_mcuHeight} pixels");
        Console.WriteLine($"MCU grid: {_mcusPerRow} cols × {_mcuRows} rows = {_mcusPerRow * _mcuRows} total MCUs");
        Console.WriteLine($"Expected total blocks: {_mcusPerRow * _mcuRows * 4}");

        _bitReader = new BitReader(_stream!);

        // Allocate component buffers based on MCU-aligned dimensions
        for (var i = 0; i < _componentCount; i++)
        {
            // Buffer must accommodate all blocks from all MCUs
            int blocksH = _mcusPerRow * _components[i].BlocksPerMcuH;
            int blocksV = _mcuRows * _components[i].BlocksPerMcuV;
            int bufferSize = blocksH * blocksV * 64;
            _components[i].Data = new byte[bufferSize];
        }

        // Reset DC predictors
        var dcPredictors = new int[_componentCount];

        // Decode MCUs in proper MCU-by-MCU order
        // JPEG spec requires: for each MCU { for each component { for blockV { for blockH }}}
        var mcuCount = 0;

        for (var mcuRow = 0; mcuRow < _mcuRows; mcuRow++)
        {
            for (var mcuCol = 0; mcuCol < _mcusPerRow; mcuCol++)
            {
                // Handle restart interval before processing MCU
                if (_restartInterval > 0 && mcuCount > 0 && mcuCount % _restartInterval == 0)
                {
                    // Restart markers are detected DURING bit reading by ReadByteWithStuffing()
                    // When a marker is encountered, it's stored in _nextMarker and reading stops
                    // The marker should already be detected by now (during previous MCU's decode)

                    Console.WriteLine($"At MCU {mcuCount}: Checking for restart marker");
                    Console.WriteLine($"  BitReader state: BitsInBuffer={_bitReader!.BitsInBuffer}, Marker=0x{_bitReader.GetMarker():X2}");

                    // Check if we're at a marker (should already be detected)
                    byte marker = _bitReader.GetMarker();
                    if (marker >= 0xD0 && marker <= 0xD7)
                    {
                        // Valid RST marker
                        int expectedRstNum = ((mcuCount / _restartInterval) - 1) % 8;
                        int actualRstNum = marker - 0xD0;
                        Console.WriteLine($"Found RST{actualRstNum} marker at MCU {mcuCount} (expected RST{expectedRstNum})");

                        if (actualRstNum != expectedRstNum)
                        {
                            Console.WriteLine($"  WARNING: RST marker mismatch!");
                        }

                        // Clear the marker flag to allow reading to continue
                        _bitReader.ClearMarker();

                        // After a restart marker, the bit stream restarts at a byte boundary
                        // Discard any remaining bits and align to byte boundary
                        Console.WriteLine($"  Buffer before alignment: bits={_bitReader.BitsInBuffer}, buffer=0x{_bitReader.GetStreamPosition():X}");
                        _bitReader.AlignToByte();
                        Console.WriteLine($"  Aligned to byte boundary, bits={_bitReader.BitsInBuffer}");
                    }
                    else if (marker != 0)
                    {
                        Console.WriteLine($"  WARNING: Expected RST marker but found marker 0x{marker:X2}");
                    }
                    else
                    {
                        Console.WriteLine($"  ERROR: No RST marker detected at expected position!");
                        throw new InvalidDataException($"Expected RST marker at MCU {mcuCount} but found none");
                    }

                    // Reset DC predictors
                    Console.WriteLine($"Resetting DC predictors from: {string.Join(", ", dcPredictors)}");
                    Array.Clear(dcPredictors, 0, dcPredictors.Length);
                    Console.WriteLine($"DC predictors after reset: {string.Join(", ", dcPredictors)}");
                }

                try
                {
                    DecodeMcu(mcuRow, mcuCol, dcPredictors);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"DECODE FAILED at MCU ({mcuRow},{mcuCol}) mcuCount={mcuCount}");
                    Console.WriteLine($"  Expected next restart at MCU {((mcuCount / _restartInterval) + 1) * _restartInterval}");
                    Console.WriteLine($"  BitReader state: bitsInBuffer={_bitReader!.BitsInBuffer}, marker={_bitReader.GetMarker():X2}");
                    throw;
                }

                mcuCount++;
            }
        }

        // Convert or return raw components
        return convertToRgb
            ? ConvertToRgb()
            : GetRawComponents();
    }

    /// <summary>
    /// Gets raw component data without color conversion.
    /// Returns interleaved component data (e.g., C,M,Y,K,C,M,Y,K,... for CMYK).
    /// </summary>
    private byte[] GetRawComponents()
    {
        var output = new byte[_width * _height * _componentCount];

        for (var y = 0; y < _height; y++)
        {
            for (var x = 0; x < _width; x++)
            {
                int outputIdx = (y * _width + x) * _componentCount;

                for (var c = 0; c < _componentCount; c++)
                {
                    JpegComponent comp = _components[c];
                    int blocksPerRow = _mcusPerRow * comp.BlocksPerMcuH;

                    // Map to component coordinates (handle subsampling)
                    int compX = x * comp.Width / _width;
                    int compY = y * comp.Height / _height;

                    int blockX = compX / 8;
                    int blockY = compY / 8;
                    int pixelX = compX % 8;
                    int pixelY = compY % 8;

                    int offset = (blockY * blocksPerRow + blockX) * 64 + pixelY * 8 + pixelX;
                    output[outputIdx + c] = comp.Data![offset];
                }
            }
        }

        return output;
    }

    /// <summary>
    /// Decodes a complete MCU (all blocks for all components).
    /// JPEG spec order: for each component { for blockV { for blockH }}.
    /// </summary>
    private void DecodeMcu(int mcuRow, int mcuCol, int[] dcPredictors)
    {
        Span<int> coeffs = stackalloc int[64];
        Span<float> dctBlock = stackalloc float[64];
        Span<byte> pixelBlock = stackalloc byte[64];

        // Iterate through components in scan order (Y, Cb, Cr)
        for (var compIdx = 0; compIdx < _componentCount; compIdx++)
        {
            JpegComponent comp = _components[compIdx];
            HuffmanTable dcTable = _dcTables[comp.DcTableId]!;
            HuffmanTable acTable = _acTables[comp.AcTableId]!;
            int[] quantTable = _quantTables[comp.QuantTableId]!;

            // DEBUG: Print table IDs for first MCU
            if (mcuRow == 0 && mcuCol == 0)
            {
                Console.WriteLine($"DEBUG: MCU(0,0) comp={compIdx} DcTableId={comp.DcTableId}, AcTableId={comp.AcTableId}, QuantTableId={comp.QuantTableId}, BlocksPerMcuH={comp.BlocksPerMcuH}, BlocksPerMcuV={comp.BlocksPerMcuV}");
            }

            // For this component, decode all blocks in order: blockV → blockH
            for (var blockV = 0; blockV < comp.BlocksPerMcuV; blockV++)
            {
                for (var blockH = 0; blockH < comp.BlocksPerMcuH; blockH++)
                {
                    // DEBUG: Print DC predictor BEFORE decode (all blocks in first MCU)
                    bool debugThisBlock = (mcuRow == 0 && mcuCol == 0);
                    if (debugThisBlock)
                    {
                        Console.WriteLine($"DEBUG: MCU(0,0) comp={compIdx} blockV={blockV} blockH={blockH} DC predictor BEFORE = {dcPredictors[compIdx]}");
                    }

                    // Decode coefficients
                    DecodeBlock(dcTable, acTable, coeffs, ref dcPredictors[compIdx], debugThisBlock, compIdx);

                    // DEBUG: Print DC coefficient BEFORE dequantization
                    if (mcuRow == 0 && mcuCol == 0 && compIdx >= 1 && blockH == 0 && blockV == 0)
                    {
                        Console.WriteLine($"DEBUG: MCU(0,0) comp={compIdx} DC delta (raw) = {coeffs[0]}, DC predictor AFTER = {dcPredictors[compIdx]}, quant[0] = {quantTable[0]}");
                    }

                    // Dequantize
                    Quantization.DequantizeFromInt(coeffs, quantTable, dctBlock);

                    // DEBUG: Print DC coefficient for first few Cb/Cr blocks
                    if (mcuRow == 0 && mcuCol == 0 && compIdx >= 1 && blockH == 0 && blockV == 0)
                    {
                        Console.WriteLine($"DEBUG: MCU(0,0) comp={compIdx} DC coeff after dequant = {dctBlock[0]:F2}");
                    }

                    // Inverse DCT
                    Dct.InverseDctToBytes(dctBlock, pixelBlock);

                    // DEBUG: Print first pixel value for first few Cb/Cr blocks
                    if (mcuRow == 0 && mcuCol == 0 && compIdx >= 1 && blockH == 0 && blockV == 0)
                    {
                        Console.WriteLine($"DEBUG: MCU(0,0) comp={compIdx} first pixel after IDCT = {pixelBlock[0]}");
                    }

                    // Store in the component buffer
                    int compBlocksPerRow = _mcusPerRow * comp.BlocksPerMcuH;
                    int destBlockX = mcuCol * comp.BlocksPerMcuH + blockH;
                    int destBlockY = mcuRow * comp.BlocksPerMcuV + blockV;

                    // Check block indices, not pixel positions (JPEG allows partial blocks at edges)
                    int compBlocksPerColumn = _mcuRows * comp.BlocksPerMcuV;
                    if (destBlockX >= compBlocksPerRow || destBlockY >= compBlocksPerColumn) continue;
                    int destOffset = (destBlockY * compBlocksPerRow + destBlockX) * 64;

                    pixelBlock.CopyTo(comp.Data.AsSpan(destOffset));
                }
            }
        }
    }

    /// <summary>
    /// Decodes a single 8x8 block of coefficients.
    /// </summary>
    private void DecodeBlock(HuffmanTable dcTable, HuffmanTable acTable, Span<int> coeffs, ref int dcPredictor, bool debug = false, int compIdx = -1)
    {
        coeffs.Clear();

        // Debug: track state before decode
        long streamPosBefore = _bitReader!.GetStreamPosition();
        int bitsBefore = _bitReader.BitsInBuffer;

        // Decode DC coefficient
        int dcSize = dcTable.Decode(_bitReader!);
        if (dcSize < 0)
        {
            Console.WriteLine($"  DECODE DC FAILED: bitsInBuffer={_bitReader.BitsInBuffer}, streamPos=0x{_bitReader.GetStreamPosition():X6}, marker={_bitReader.GetMarker():X2}");
            Console.WriteLine($"  dcPredictor before={dcPredictor}");
            Console.WriteLine($"  Stream/bits before decode: pos=0x{streamPosBefore:X6}, bits={bitsBefore}");
            throw new InvalidDataException("Failed to decode DC coefficient");
        }

        var dcDiff = 0;
        int rawBits = 0;
        if (dcSize > 0)
        {
            rawBits = _bitReader.ReadBits(dcSize);
            dcDiff = BitReader.Extend(rawBits, dcSize);
        }

        if (debug)
        {
            long posAfterSize = _bitReader!.GetStreamPosition();
            int bitsAfterSize = _bitReader.BitsInBuffer;
            long posAfterValue = _bitReader!.GetStreamPosition();
            int bitsAfterValue = _bitReader.BitsInBuffer;
            Console.WriteLine($"DEBUG: comp={compIdx} dcSize={dcSize} [pos before=0x{streamPosBefore:X}, bits={bitsBefore}]");
            Console.WriteLine($"DEBUG: comp={compIdx} rawBits=0x{rawBits:X} ({rawBits}), dcDiff after Extend={dcDiff} [pos after=0x{posAfterValue:X}, bits={bitsAfterValue}]");
        }

        dcPredictor += dcDiff;
        coeffs[0] = dcPredictor;

        // Decode AC coefficients
        var index = 1;
        while (index < 64)
        {
            int symbol = acTable.Decode(_bitReader);
            if (symbol < 0)
                throw new InvalidDataException("Failed to decode AC coefficient");

            if (symbol == 0)
            {
                // EOB - remaining coefficients are zero
                break;
            }

            int runLength = (symbol >> 4) & 0x0F;
            int acSize = symbol & 0x0F;

            if (acSize == 0)
            {
                if (runLength == 15)
                {
                    // ZRL - 16 zeros
                    index += 16;
                }
                else
                {
                    // Invalid
                    break;
                }
            }
            else
            {
                index += runLength;
                if (index >= 64)
                    break;

                int acValue = _bitReader.ReadBits(acSize);
                acValue = BitReader.Extend(acValue, acSize);

                // Convert from zigzag to natural order
                coeffs[JpegConstants.ZigzagOrder[index]] = acValue;
                index++;
            }
        }
    }

    /// <summary>
    /// Converts decoded component data to RGB.
    /// </summary>
    private byte[] ConvertToRgb()
    {
        var rgb = new byte[_width * _height * 3];

        switch (_componentCount)
        {
            case 1:
                // Grayscale
                ConvertGrayscaleToRgb(rgb);
                break;
            case 3:
                // YCbCr
                ConvertYCbCrToRgb(rgb);
                break;
            case 4:
                // CMYK or YCCK
                ConvertCmykToRgb(rgb);
                break;
            default:
                throw new NotSupportedException($"Unsupported component count: {_componentCount}");
        }

        return rgb;
    }

    /// <summary>
    /// Converts CMYK or YCCK to RGB.
    /// </summary>
    private void ConvertCmykToRgb(byte[] rgb)
    {
        // Get raw component data first
        byte[] cmyk = GetRawComponents();

        bool isYcck = _hasAdobeMarker && _adobeColorTransform == 2;
        bool isInvertedCmyk = _hasAdobeMarker && _adobeColorTransform == 0;

        for (var i = 0; i < _width * _height; i++)
        {
            int srcIdx = i * 4;
            int dstIdx = i * 3;

            byte c, m, y, k;

            if (isYcck)
            {
                // YCCK: Convert YCbCr to inverted CMY, K is stored as-is
                float yVal = cmyk[srcIdx];
                float cb = cmyk[srcIdx + 1];
                float cr = cmyk[srcIdx + 2];
                byte kStored = cmyk[srcIdx + 3];

                // YCbCr → RGB (ITU-R BT.601)
                // These are the INVERTED CMY values
                float rPrime = yVal + 1.402f * (cr - 128);
                float gPrime = yVal - 0.344136f * (cb - 128) - 0.714136f * (cr - 128);
                float bPrime = yVal + 1.772f * (cb - 128);

                // Invert R',G',B' to get actual C,M,Y
                c = ClampToByte(255 - rPrime);
                m = ClampToByte(255 - gPrime);
                y = ClampToByte(255 - bPrime);
                k = kStored;
            }
            else if (isInvertedCmyk)
            {
                // Inverted CMYK: need to uninvert
                c = (byte)(255 - cmyk[srcIdx]);
                m = (byte)(255 - cmyk[srcIdx + 1]);
                y = (byte)(255 - cmyk[srcIdx + 2]);
                k = (byte)(255 - cmyk[srcIdx + 3]);
            }
            else
            {
                // Standard CMYK
                c = cmyk[srcIdx];
                m = cmyk[srcIdx + 1];
                y = cmyk[srcIdx + 2];
                k = cmyk[srcIdx + 3];
            }

            // CMYK to RGB conversion
            // R = 255 * (1 - C/255) * (1 - K/255)
            // G = 255 * (1 - M/255) * (1 - K/255)
            // B = 255 * (1 - Y/255) * (1 - K/255)
            float kFactor = 1 - k / 255f;
            rgb[dstIdx] = ClampToByte((255 - c) * kFactor);
            rgb[dstIdx + 1] = ClampToByte((255 - m) * kFactor);
            rgb[dstIdx + 2] = ClampToByte((255 - y) * kFactor);
        }
    }

    private static byte ClampToByte(float value)
    {
        return (byte)Math.Clamp((int)(value + 0.5f), 0, 255);
    }

    /// <summary>
    /// Converts grayscale to RGB.
    /// </summary>
    private void ConvertGrayscaleToRgb(byte[] rgb)
    {
        JpegComponent comp = _components[0];
        // Use MCU-based block count to match how IDCT stores the data
        int blocksPerRow = _mcusPerRow * comp.BlocksPerMcuH;

        for (var y = 0; y < _height; y++)
        {
            for (var x = 0; x < _width; x++)
            {
                int blockX = x / 8;
                int blockY = y / 8;
                int pixelX = x % 8;
                int pixelY = y % 8;

                int offset = (blockY * blocksPerRow + blockX) * 64 + pixelY * 8 + pixelX;
                byte gray = comp.Data![offset];

                int rgbOffset = (y * _width + x) * 3;
                rgb[rgbOffset] = gray;
                rgb[rgbOffset + 1] = gray;
                rgb[rgbOffset + 2] = gray;
            }
        }
    }

    /// <summary>
    /// Converts YCbCr to RGB with proper upsampling.
    /// </summary>
    private void ConvertYCbCrToRgb(byte[] rgb)
    {
        JpegComponent yComp = _components[0];
        JpegComponent cbComp = _components[1];
        JpegComponent crComp = _components[2];

        // Use MCU-based block count to match how IDCT stores the data
        int yBlocksPerRow = _mcusPerRow * yComp.BlocksPerMcuH;
        int cbBlocksPerRow = _mcusPerRow * cbComp.BlocksPerMcuH;
        int crBlocksPerRow = _mcusPerRow * crComp.BlocksPerMcuH;

        for (var y = 0; y < _height; y++)
        {
            for (var x = 0; x < _width; x++)
            {
                // Y component (full resolution)
                int yBlockX = x / 8;
                int yBlockY = y / 8;
                int yPixelX = x % 8;
                int yPixelY = y % 8;
                int yOffset = (yBlockY * yBlocksPerRow + yBlockX) * 64 + yPixelY * 8 + yPixelX;
                byte yVal = yComp.Data![yOffset];

                // Cb/Cr components (may be subsampled)
                int cbX = x * cbComp.Width / _width;
                int crX = x * crComp.Width / _width;
                int cbY = y * cbComp.Height / _height;
                int crY = y * crComp.Height / _height;

                int cbBlockX = cbX / 8;
                int cbBlockY = cbY / 8;
                int cbPixelX = cbX % 8;
                int cbPixelY = cbY % 8;
                int cbOffset = (cbBlockY * cbBlocksPerRow + cbBlockX) * 64 + cbPixelY * 8 + cbPixelX;
                byte cbVal = cbComp.Data![cbOffset];

                int crBlockX = crX / 8;
                int crBlockY = crY / 8;
                int crPixelX = crX % 8;
                int crPixelY = crY % 8;
                int crOffset = (crBlockY * crBlocksPerRow + crBlockX) * 64 + crPixelY * 8 + crPixelX;
                byte crVal = crComp.Data![crOffset];

                // Convert to RGB
                ColorConversion.YCbCrToRgb(yVal, cbVal, crVal, out byte r, out byte g, out byte b);

                int rgbOffset = (y * _width + x) * 3;
                rgb[rgbOffset] = r;
                rgb[rgbOffset + 1] = g;
                rgb[rgbOffset + 2] = b;
            }
        }
    }

    /// <summary>
    /// Component information.
    /// </summary>
    private class JpegComponent
    {
        public int Id;
        public int HorizontalSampling;
        public int VerticalSampling;
        public int QuantTableId;
        public int DcTableId;
        public int AcTableId;
        public int Width;
        public int Height;
        public int BlocksPerMcuH;
        public int BlocksPerMcuV;
        public byte[]? Data;
    }
}
