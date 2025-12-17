using System;
using System.Collections.Generic;
using System.IO;

namespace ImageLibrary.Jbig2;

/// <summary>
/// JBIG2 image decoder.
/// Decodes JBIG2 codestreams to bi-level bitmaps.
/// </summary>
public sealed class Jbig2Decoder
{
    private readonly byte[] _data;
    private readonly byte[]? _globalData;
    private readonly Jbig2DecoderOptions _options;
    private readonly Dictionary<uint, object> _segments = new();
    private readonly Dictionary<uint, Bitmap> _pages = new();
    private int _segmentsProcessed;
    private long _decodeOperations;

    /// <summary>
    /// Creates a decoder for embedded JBIG2 data (as used in PDF).
    /// </summary>
    /// <param name="data">JBIG2 segment data</param>
    /// <param name="globalData">Optional global segment data (JBIG2Globals in PDF)</param>
    /// <param name="options">Decoder options with resource limits</param>
    public Jbig2Decoder(byte[] data, byte[]? globalData = null, Jbig2DecoderOptions? options = null)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _globalData = globalData;
        _options = options ?? Jbig2DecoderOptions.Default;

        if (_data.Length == 0)
            throw new Jbig2DataException("JBIG2 data is empty");
    }

    /// <summary>
    /// Creates a decoder from a stream.
    /// </summary>
    /// <param name="stream">Stream containing JBIG2 data</param>
    /// <param name="globalData">Optional global segment data (JBIG2Globals in PDF)</param>
    /// <param name="options">Decoder options with resource limits</param>
    public Jbig2Decoder(Stream stream, byte[]? globalData = null, Jbig2DecoderOptions? options = null)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _data = ms.ToArray();
        _globalData = globalData;
        _options = options ?? Jbig2DecoderOptions.Default;

        if (_data.Length == 0)
            throw new Jbig2DataException("JBIG2 data is empty");
    }

    /// <summary>
    /// Creates a decoder from a file path.
    /// </summary>
    /// <param name="path">Path to JBIG2 file</param>
    /// <param name="globalData">Optional global segment data (JBIG2Globals in PDF)</param>
    /// <param name="options">Decoder options with resource limits</param>
    public Jbig2Decoder(string path, byte[]? globalData = null, Jbig2DecoderOptions? options = null)
    {
        _data = File.ReadAllBytes(path);
        _globalData = globalData;
        _options = options ?? Jbig2DecoderOptions.Default;

        if (_data.Length == 0)
            throw new Jbig2DataException("JBIG2 data is empty");
    }

    /// <summary>
    /// Decodes the JBIG2 data and returns the first page as a bitmap.
    /// </summary>
    public Bitmap Decode()
    {
        // Process global segments first if present
        if (_globalData != null && _globalData.Length > 0)
        {
            ProcessSegments(_globalData, isGlobal: true);
        }

        // Process main data
        ProcessSegments(_data, isGlobal: false);

        // Return first page (page 1)
        if (_pages.TryGetValue(1, out Bitmap? page))
            return page;

        throw new Jbig2DataException("No page found in JBIG2 data");
    }

    /// <summary>
    /// Decodes a specific page.
    /// </summary>
    public Bitmap DecodePage(uint pageNumber)
    {
        if (!_pages.ContainsKey(pageNumber))
        {
            // Process if not already done
            if (_globalData != null && _globalData.Length > 0)
                ProcessSegments(_globalData, isGlobal: true);
            ProcessSegments(_data, isGlobal: false);
        }

        if (_pages.TryGetValue(pageNumber, out Bitmap? page))
            return page;

        throw new Jbig2DataException($"Page {pageNumber} not found in JBIG2 data");
    }

    /// <summary>
    /// Tracks decode operations and enforces limit.
    /// </summary>
    internal void TrackDecodeOperation(long count = 1)
    {
        _decodeOperations += count;
        if (_decodeOperations > _options.MaxDecodeOperations)
            throw new Jbig2ResourceException($"Decode operation limit exceeded ({_options.MaxDecodeOperations})");
    }

    private void ProcessSegments(byte[] data, bool isGlobal)
    {
        // Check for file header
        (int offset, bool sequential, uint pageCount) = SegmentHeaderParser.ParseFileHeader(data);

        if (pageCount > _options.MaxPages)
            throw new Jbig2ResourceException($"Page count {pageCount} exceeds limit {_options.MaxPages}");

        var reader = new BitReader(data, offset);

        while (!reader.IsAtEnd && reader.RemainingBytes >= 11) // Minimum segment header size
        {
            // Check segment count limit
            if (_segmentsProcessed >= _options.MaxSegments)
                throw new Jbig2ResourceException($"Segment count exceeds limit {_options.MaxSegments}");

            try
            {
                SegmentHeader header = SegmentHeaderParser.Parse(reader, _options);

                if (header.Type == SegmentType.EndOfFile)
                    break;

                // Validate segment data length
                if (!header.IsDataLengthUnknown && header.DataLength > _options.MaxSegmentDataLength)
                    throw new Jbig2ResourceException($"Segment data length {header.DataLength} exceeds limit {_options.MaxSegmentDataLength}");

                // Read segment data
                int dataOffset = reader.BytePosition;
                byte[] segmentData;

                if (header.IsDataLengthUnknown)
                {
                    throw new Jbig2UnsupportedException("Unknown segment length not yet supported");
                }

                // Validate we have enough data
                if (reader.RemainingBytes < (int)header.DataLength)
                    throw new Jbig2DataException($"Segment data truncated: expected {header.DataLength} bytes, have {reader.RemainingBytes}");

                segmentData = new byte[header.DataLength];
                Array.Copy(data, dataOffset, segmentData, 0, (int)header.DataLength);
                reader.SkipBytes((int)header.DataLength);

                ProcessSegment(header, segmentData);
                _segmentsProcessed++;
            }
            catch (Jbig2Exception)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new Jbig2DataException($"Error processing segment: {ex.Message}", ex);
            }
        }
    }

    private void ProcessSegment(SegmentHeader header, byte[] data)
    {
        switch (header.Type)
        {
            case SegmentType.PageInformation:
                ProcessPageInformation(header, data);
                break;

            case SegmentType.EndOfPage:
                // Page complete, nothing to do
                break;

            case SegmentType.ImmediateGenericRegion:
            case SegmentType.ImmediateLosslessGenericRegion:
                ProcessGenericRegion(header, data, immediate: true);
                break;

            case SegmentType.IntermediateGenericRegion:
                ProcessGenericRegion(header, data, immediate: false);
                break;

            case SegmentType.SymbolDictionary:
                ProcessSymbolDictionary(header, data);
                break;

            case SegmentType.ImmediateTextRegion:
            case SegmentType.ImmediateLosslessTextRegion:
                ProcessTextRegion(header, data, immediate: true);
                break;

            case SegmentType.IntermediateTextRegion:
                ProcessTextRegion(header, data, immediate: false);
                break;

            case SegmentType.Tables:
                ProcessTables(header, data);
                break;

            case SegmentType.PatternDictionary:
                ProcessPatternDictionary(header, data);
                break;

            case SegmentType.ImmediateHalftoneRegion:
            case SegmentType.ImmediateLosslessHalftoneRegion:
                ProcessHalftoneRegion(header, data, immediate: true);
                break;

            case SegmentType.IntermediateHalftoneRegion:
                ProcessHalftoneRegion(header, data, immediate: false);
                break;

            case SegmentType.ImmediateGenericRefinementRegion:
            case SegmentType.ImmediateLosslessGenericRefinementRegion:
                ProcessGenericRefinementRegion(header, data, immediate: true);
                break;

            case SegmentType.IntermediateGenericRefinementRegion:
                ProcessGenericRefinementRegion(header, data, immediate: false);
                break;
        }
    }

    private void ProcessPageInformation(SegmentHeader header, byte[] data)
    {
        // T.88 7.4.8 - Page information segment
        if (data.Length < 19)
            throw new Jbig2DataException("Page information segment too short");

        // Check page count limit
        if (_pages.Count >= _options.MaxPages)
            throw new Jbig2ResourceException($"Page count exceeds limit {_options.MaxPages}");

        var reader = new BitReader(data);

        uint width = reader.ReadUInt32BigEndian();
        uint height = reader.ReadUInt32BigEndian();
        uint xResolution = reader.ReadUInt32BigEndian();
        uint yResolution = reader.ReadUInt32BigEndian();

        byte flags = reader.ReadByte();
        bool isLossless = (flags & 0x01) != 0;
        bool requiresAuxBuffer = (flags & 0x02) != 0;
        int defaultCombOp = (flags >> 3) & 0x03;
        bool isStriped = (flags & 0x80) != 0;

        ushort stripingInfo = 0;
        if (isStriped)
            stripingInfo = reader.ReadUInt16BigEndian();

        // Validate dimensions
        // Height of 0xFFFFFFFF means unknown height (streaming) - use 1 initially
        var pageWidth = (int)width;
        int pageHeight = height == 0xFFFFFFFF ? 1 : (int)height;

        // Validate against limits (use 1 for unknown height validation)
        _options.ValidateDimensions(pageWidth, pageHeight == 0 ? 1 : pageHeight, "Page");

        var pageBitmap = new Bitmap(pageWidth, pageHeight, _options);

        // Default fill based on combination operator
        if (defaultCombOp == 1) // AND - need white background
            pageBitmap.Fill(1);

        _pages[header.PageAssociation] = pageBitmap;
    }

    private void ProcessGenericRegion(SegmentHeader header, byte[] data, bool immediate)
    {
        // T.88 7.4.5 - Generic region segment
        if (data.Length < 18)
            throw new Jbig2DataException("Generic region segment too short");

        var reader = new BitReader(data);

        // Region segment information field (T.88 7.4.1)
        uint width = reader.ReadUInt32BigEndian();
        uint height = reader.ReadUInt32BigEndian();
        uint x = reader.ReadUInt32BigEndian();
        uint y = reader.ReadUInt32BigEndian();
        byte combOp = reader.ReadByte();

        // Validate region dimensions
        _options.ValidateDimensions((int)width, (int)height, "Generic region");

        // Generic region segment flags
        byte flags = reader.ReadByte();
        bool useMmr = (flags & 0x01) != 0;
        int template = (flags >> 1) & 0x03;
        bool typicalPrediction = (flags & 0x08) != 0;
        bool useExtTemplates = (flags & 0x10) != 0;

        Bitmap regionBitmap;

        if (useMmr)
        {
            // MMR-coded generic region (T.88 6.2.6)
            // No adaptive template pixels for MMR - data starts immediately
            int mmrDataOffset = reader.BytePosition;
            int mmrDataLength = data.Length - mmrDataOffset;

            if (mmrDataLength <= 0)
                throw new Jbig2DataException("No MMR data in generic region");

            var mmrDecoder = new MmrDecoder(data, mmrDataOffset, mmrDataLength, (int)width, (int)height);
            regionBitmap = mmrDecoder.Decode();
        }
        else
        {
            // Read adaptive template pixels
            (int dx, int dy)[] adaptivePixels;

            if (template == 0)
            {
                // 4 adaptive template pixels for template 0
                adaptivePixels = new (int, int)[4];
                adaptivePixels[0] = ((sbyte)reader.ReadByte(), (sbyte)reader.ReadByte());
                adaptivePixels[1] = ((sbyte)reader.ReadByte(), (sbyte)reader.ReadByte());
                adaptivePixels[2] = ((sbyte)reader.ReadByte(), (sbyte)reader.ReadByte());
                adaptivePixels[3] = ((sbyte)reader.ReadByte(), (sbyte)reader.ReadByte());
            }
            else
            {
                // 1 adaptive template pixel for templates 1-3
                var atx = (sbyte)reader.ReadByte();
                var aty = (sbyte)reader.ReadByte();
                adaptivePixels = [(atx, aty)];
            }

            // Create arithmetic decoder from remaining data
            int arithmeticDataOffset = reader.BytePosition;
            int arithmeticDataLength = data.Length - arithmeticDataOffset;

            if (arithmeticDataLength <= 0)
                throw new Jbig2DataException("No arithmetic coded data in generic region");

            var arithmeticDecoder = new ArithmeticDecoder(data, arithmeticDataOffset, arithmeticDataLength, this);
            var genericDecoder = new GenericRegionDecoder(
                arithmeticDecoder,
                template,
                adaptivePixels,
                typicalPrediction,
                _options);

            regionBitmap = genericDecoder.Decode((int)width, (int)height);
        }

        if (immediate)
        {
            // Composite onto page
            if (_pages.TryGetValue(header.PageAssociation, out Bitmap? pageBitmap))
            {
                // Validate blit coordinates
                if (x > int.MaxValue || y > int.MaxValue)
                    throw new Jbig2DataException($"Region position overflow: ({x}, {y})");

                var op = (CombinationOperator)(combOp & 0x07);
                pageBitmap.Blit(regionBitmap, (int)x, (int)y, op);
            }
        }
        else
        {
            // Store for later reference
            _segments[header.SegmentNumber] = regionBitmap;
        }
    }

    private void ProcessSymbolDictionary(SegmentHeader header, byte[] data)
    {
        // T.88 7.4.2 - Symbol dictionary segment
        if (data.Length < 10)
            throw new Jbig2DataException("Symbol dictionary segment too short");

        var reader = new BitReader(data);

        // 7.4.2.1.1 - Symbol dictionary flags (2 bytes)
        ushort flags = reader.ReadUInt16BigEndian();
        bool useHuffman = (flags & 0x0001) != 0;
        bool useRefinementAgg = (flags & 0x0002) != 0;

        // T.88 Table 10 - Symbol dictionary segment flags
        // SDTEMPLATE is ALWAYS at bits 10-11, regardless of SDHUFF
        // SDRTEMPLATE is ALWAYS at bit 12 (if SDREFAGG=1)
        // Bits 2-7 are Huffman table selections (only used if SDHUFF=1)
        int huffmanDH = (flags >> 2) & 0x03;   // Bits 2-3: SDHUFFDH (if SDHUFF=1)
        int huffmanDW = (flags >> 4) & 0x03;   // Bits 4-5: SDHUFFDW (if SDHUFF=1)
        int huffmanBMSIZE = (flags >> 6) & 0x01; // Bit 6: SDHUFFBMSIZE (if SDHUFF=1)
        int huffmanAGGINST = (flags >> 7) & 0x01; // Bit 7: SDHUFFAGGINST (if SDHUFF=1 and SDREFAGG=1)
        bool contextUsed = (flags & 0x0100) != 0;   // Bit 8
        bool contextRetained = (flags & 0x0200) != 0; // Bit 9
        int sdTemplate = (flags >> 10) & 0x03;   // Bits 10-11: SDTEMPLATE
        int sdrTemplate = (flags >> 12) & 0x01;  // Bit 12: SDRTEMPLATE (if SDREFAGG=1)

        // Read adaptive template pixels
        (int dx, int dy)[] adaptivePixels;
        if (!useHuffman)
        {
            if (sdTemplate == 0)
            {
                adaptivePixels = new (int, int)[4];
                adaptivePixels[0] = ((sbyte)reader.ReadByte(), (sbyte)reader.ReadByte());
                adaptivePixels[1] = ((sbyte)reader.ReadByte(), (sbyte)reader.ReadByte());
                adaptivePixels[2] = ((sbyte)reader.ReadByte(), (sbyte)reader.ReadByte());
                adaptivePixels[3] = ((sbyte)reader.ReadByte(), (sbyte)reader.ReadByte());
            }
            else
            {
                adaptivePixels = new (int, int)[1];
                adaptivePixels[0] = ((sbyte)reader.ReadByte(), (sbyte)reader.ReadByte());
            }
        }
        else
        {
            adaptivePixels = [];
        }

        // Read refinement adaptive template pixels
        (int dx, int dy)[] refinementAdaptivePixels;
        if (useRefinementAgg && sdrTemplate == 0)
        {
            refinementAdaptivePixels = new (int, int)[2];
            refinementAdaptivePixels[0] = ((sbyte)reader.ReadByte(), (sbyte)reader.ReadByte());
            refinementAdaptivePixels[1] = ((sbyte)reader.ReadByte(), (sbyte)reader.ReadByte());
        }
        else
        {
            refinementAdaptivePixels = [];
        }

        // 7.4.2.1.2 - Number of exported symbols (4 bytes)
        uint numExportedSymbols = reader.ReadUInt32BigEndian();

        // 7.4.2.1.3 - Number of new symbols (4 bytes)
        uint numNewSymbols = reader.ReadUInt32BigEndian();

        // Collect input symbols and custom Huffman tables from referred segments
        var inputSymbols = new SymbolDictionary();
        var customTables = new List<HuffmanTable>();
        foreach (uint refSegNum in header.ReferredToSegments)
        {
            if (_segments.TryGetValue(refSegNum, out object? refSeg))
            {
                if (refSeg is SymbolDictionary refDict)
                {
                    for (var i = 0; i < refDict.Count; i++)
                    {
                        inputSymbols.Add(refDict[i]);
                    }
                }
                else if (refSeg is HuffmanTable huffTable)
                {
                    customTables.Add(huffTable);
                }
            }
        }

        // Create parameters
        var sdParams = new SymbolDictionaryParams
        {
            UseHuffman = useHuffman,
            UseRefinementAgg = useRefinementAgg,
            NumInputSymbols = inputSymbols.Count,
            NumNewSymbols = (int)numNewSymbols,
            NumExportedSymbols = (int)numExportedSymbols,
            Template = sdTemplate,
            RefinementTemplate = sdrTemplate,
            AdaptivePixels = adaptivePixels,
            RefinementAdaptivePixels = refinementAdaptivePixels,
            HuffmanDH = huffmanDH,
            HuffmanDW = huffmanDW,
            HuffmanBMSIZE = huffmanBMSIZE,
            HuffmanAGGINST = huffmanAGGINST
        };

        // Create decoder from remaining data
        int dataOffset = reader.BytePosition;
        int dataLength = data.Length - dataOffset;

        if (dataLength <= 0)
            throw new Jbig2DataException("No data in symbol dictionary segment");

        SymbolDictionary exportedSymbols;

        if (useHuffman)
        {
            // Use Huffman decoder
            var huffmanDecoder = new HuffmanDecoder(data, dataOffset, dataLength);
            HuffmanTable[]? customTablesArray = customTables.Count > 0 ? customTables.ToArray() : null;
            var sdDecoder = new HuffmanSymbolDictionaryDecoder(huffmanDecoder, sdParams, inputSymbols, _options, customTablesArray);
            exportedSymbols = sdDecoder.Decode();
        }
        else
        {
            // Use arithmetic decoder
            var arithmeticDecoder = new ArithmeticDecoder(data, dataOffset, dataLength, this);
            var sdDecoder = new SymbolDictionaryDecoder(arithmeticDecoder, sdParams, inputSymbols, _options);
            exportedSymbols = sdDecoder.Decode();
        }

        // Store the symbol dictionary
        _segments[header.SegmentNumber] = exportedSymbols;
    }

    private void ProcessTextRegion(SegmentHeader header, byte[] data, bool immediate)
    {
        // T.88 7.4.3 - Text region segment
        if (data.Length < 17)
            throw new Jbig2DataException("Text region segment too short");

        var reader = new BitReader(data);

        // Region segment information field (T.88 7.4.1)
        uint width = reader.ReadUInt32BigEndian();
        uint height = reader.ReadUInt32BigEndian();
        uint x = reader.ReadUInt32BigEndian();
        uint y = reader.ReadUInt32BigEndian();
        byte combOp = reader.ReadByte();

        _options.ValidateDimensions((int)width, (int)height, "Text region");

        // 7.4.3.1.1 - Text region segment flags (2 bytes)
        ushort flags = reader.ReadUInt16BigEndian();
        bool useHuffman = (flags & 0x0001) != 0;
        bool useRefinement = (flags & 0x0002) != 0;
        int logStripSize = (flags >> 2) & 0x03;
        int refCorner = (flags >> 4) & 0x03;
        bool transposed = (flags & 0x0040) != 0;
        int sbCombOp = (flags >> 7) & 0x03;
        int defaultPixel = (flags >> 9) & 0x01;
        int dsOffset = (flags >> 10) & 0x1F;
        if ((dsOffset & 0x10) != 0) // Sign extend
            dsOffset |= unchecked((int)0xFFFFFFE0);
        int sbrTemplate = (flags >> 15) & 0x01;

        int stripSize = 1 << logStripSize;

        // Read refinement adaptive template pixels if needed
        (int dx, int dy)[] refinementAdaptivePixels;
        if (useRefinement && sbrTemplate == 0)
        {
            refinementAdaptivePixels = new (int, int)[2];
            refinementAdaptivePixels[0] = ((sbyte)reader.ReadByte(), (sbyte)reader.ReadByte());
            refinementAdaptivePixels[1] = ((sbyte)reader.ReadByte(), (sbyte)reader.ReadByte());
        }
        else
        {
            refinementAdaptivePixels = [];
        }

        // Huffman flags (only present if useHuffman)
        int huffmanFS = 0, huffmanDS = 0, huffmanDT = 0;
        int huffmanRDW = 0, huffmanRDH = 0, huffmanRDX = 0, huffmanRDY = 0, huffmanRSIZE = 0;

        if (useHuffman)
        {
            // 7.4.3.1.2 - Text region Huffman flags (2 bytes)
            ushort huffFlags = reader.ReadUInt16BigEndian();
            huffmanFS = huffFlags & 0x03;
            huffmanDS = (huffFlags >> 2) & 0x03;
            huffmanDT = (huffFlags >> 4) & 0x03;
            huffmanRDW = (huffFlags >> 6) & 0x03;
            huffmanRDH = (huffFlags >> 8) & 0x03;
            huffmanRDX = (huffFlags >> 10) & 0x03;
            huffmanRDY = (huffFlags >> 12) & 0x03;
            huffmanRSIZE = (huffFlags >> 14) & 0x01;
        }

        // 7.4.3.1.4 - Number of symbol instances (4 bytes)
        uint numInstances = reader.ReadUInt32BigEndian();

        // Collect symbol dictionaries and custom Huffman tables from referred segments
        var symbolDicts = new List<SymbolDictionary>();
        var customTables = new List<HuffmanTable>();
        foreach (uint refSegNum in header.ReferredToSegments)
        {
            if (_segments.TryGetValue(refSegNum, out object? refSeg))
            {
                if (refSeg is SymbolDictionary refDict)
                {
                    symbolDicts.Add(refDict);
                }
                else if (refSeg is HuffmanTable huffTable)
                {
                    customTables.Add(huffTable);
                }
            }
        }

        if (symbolDicts.Count == 0)
            throw new Jbig2DataException("No symbol dictionaries referenced by text region");

        // Create parameters
        var textParams = new TextRegionParams
        {
            UseHuffman = useHuffman,
            UseRefinement = useRefinement,
            StripSize = stripSize,
            LogStripSize = logStripSize,
            ReferenceCorner = (ReferenceCorner)refCorner,
            Transposed = transposed,
            CombinationOp = (CombinationOperator)sbCombOp,
            DefaultPixel = defaultPixel,
            StripDeltaOffset = dsOffset,
            RefinementTemplate = sbrTemplate,
            NumInstances = (int)numInstances,
            RefinementAdaptivePixels = refinementAdaptivePixels,
            HuffmanFS = huffmanFS,
            HuffmanDS = huffmanDS,
            HuffmanDT = huffmanDT,
            HuffmanRDW = huffmanRDW,
            HuffmanRDH = huffmanRDH,
            HuffmanRDX = huffmanRDX,
            HuffmanRDY = huffmanRDY,
            HuffmanRSIZE = huffmanRSIZE
        };

        // Decode the region
        int dataOffset = reader.BytePosition;
        int dataLength = data.Length - dataOffset;

        if (dataLength <= 0)
            throw new Jbig2DataException("No data in text region segment");

        Bitmap regionBitmap;
        if (useHuffman)
        {
            // Use Huffman decoder
            var huffDecoder = new HuffmanDecoder(data, dataOffset, dataLength);
            HuffmanTable[]? customTablesArray = customTables.Count > 0 ? customTables.ToArray() : null;
            var textDecoder = new HuffmanTextRegionDecoder(huffDecoder, textParams, symbolDicts.ToArray(), _options, customTablesArray);
            regionBitmap = textDecoder.Decode((int)width, (int)height);
        }
        else
        {
            // Use arithmetic decoder
            var arithmeticDecoder = new ArithmeticDecoder(data, dataOffset, dataLength, this);
            var textDecoder = new TextRegionDecoder(arithmeticDecoder, textParams, symbolDicts.ToArray(), _options);
            regionBitmap = textDecoder.Decode((int)width, (int)height);
        }

        if (immediate)
        {
            // Composite onto page
            if (_pages.TryGetValue(header.PageAssociation, out Bitmap? pageBitmap))
            {
                if (x > int.MaxValue || y > int.MaxValue)
                    throw new Jbig2DataException($"Region position overflow: ({x}, {y})");

                var op = (CombinationOperator)(combOp & 0x07);
                pageBitmap.Blit(regionBitmap, (int)x, (int)y, op);
            }
        }
        else
        {
            _segments[header.SegmentNumber] = regionBitmap;
        }
    }

    private void ProcessTables(SegmentHeader header, byte[] data)
    {
        // T.88 7.4.12 - Custom Huffman table segment
        if (data.Length < 1)
            throw new Jbig2DataException("Tables segment too short");

        HuffmanTable table = CustomHuffmanTableDecoder.Decode(data);

        // Store the table for later reference by symbol dictionaries and text regions
        _segments[header.SegmentNumber] = table;
    }

    private void ProcessPatternDictionary(SegmentHeader header, byte[] data)
    {
        // T.88 7.4.4 - Pattern dictionary segment
        if (data.Length < 7)
            throw new Jbig2DataException("Pattern dictionary segment too short");

        var reader = new BitReader(data);

        // 7.4.4.1 - Pattern dictionary flags (1 byte)
        byte flags = reader.ReadByte();
        bool useMmr = (flags & 0x01) != 0;
        int hdTemplate = (flags >> 1) & 0x03;

        // 7.4.4.2 - Pattern width (1 byte)
        byte patternWidth = reader.ReadByte();

        // 7.4.4.3 - Pattern height (1 byte)
        byte patternHeight = reader.ReadByte();

        // 7.4.4.4 - Largest gray value (4 bytes)
        uint grayMax = reader.ReadUInt32BigEndian();

        // Validate
        if (patternWidth == 0 || patternHeight == 0)
            throw new Jbig2DataException("Invalid pattern dimensions");
        if (grayMax > 255)
            throw new Jbig2DataException($"GrayMax too large: {grayMax}");

        // Read adaptive template pixels if needed
        (int dx, int dy)[] adaptivePixels;
        if (!useMmr && hdTemplate == 0)
        {
            // 4 AT pixels for template 0
            adaptivePixels = new (int, int)[4];
            adaptivePixels[0] = ((sbyte)reader.ReadByte(), (sbyte)reader.ReadByte());
            adaptivePixels[1] = ((sbyte)reader.ReadByte(), (sbyte)reader.ReadByte());
            adaptivePixels[2] = ((sbyte)reader.ReadByte(), (sbyte)reader.ReadByte());
            adaptivePixels[3] = ((sbyte)reader.ReadByte(), (sbyte)reader.ReadByte());
        }
        else
        {
            adaptivePixels = [];
        }

        // Create parameters
        var pdParams = new PatternDictionaryParams
        {
            PatternWidth = patternWidth,
            PatternHeight = patternHeight,
            GrayMax = (int)grayMax,
            UseMmr = useMmr,
            Template = hdTemplate,
            AdaptivePixels = adaptivePixels
        };

        // Decode the pattern dictionary
        int dataOffset = reader.BytePosition;
        int dataLength = data.Length - dataOffset;

        if (dataLength <= 0)
            throw new Jbig2DataException("No data in pattern dictionary segment");

        var pdDecoder = new PatternDictionaryDecoder(data, dataOffset, dataLength, pdParams, _options);
        PatternDictionary patternDict = pdDecoder.Decode();

        // Store the pattern dictionary
        _segments[header.SegmentNumber] = patternDict;
    }

    private void ProcessHalftoneRegion(SegmentHeader header, byte[] data, bool immediate)
    {
        // T.88 7.4.5 - Halftone region segment
        if (data.Length < 35)
            throw new Jbig2DataException("Halftone region segment too short");

        var reader = new BitReader(data);

        // Region segment information field (T.88 7.4.1)
        uint width = reader.ReadUInt32BigEndian();
        uint height = reader.ReadUInt32BigEndian();
        uint x = reader.ReadUInt32BigEndian();
        uint y = reader.ReadUInt32BigEndian();
        byte combOp = reader.ReadByte();

        _options.ValidateDimensions((int)width, (int)height, "Halftone region");

        // 7.4.5.1 - Halftone region flags (1 byte)
        byte flags = reader.ReadByte();
        bool useMmr = (flags & 0x01) != 0;
        int hdTemplate = (flags >> 1) & 0x03;
        bool enableSkip = (flags & 0x08) != 0;
        int hCombOp = (flags >> 4) & 0x07;
        int defaultPixel = (flags >> 7) & 0x01;

        // 7.4.5.2 - Grid width (4 bytes)
        uint gridWidth = reader.ReadUInt32BigEndian();

        // 7.4.5.3 - Grid height (4 bytes)
        uint gridHeight = reader.ReadUInt32BigEndian();

        // 7.4.5.4 - Grid vector X (4 bytes, signed)
        int gridVectorX = reader.ReadInt32BigEndian();

        // 7.4.5.5 - Grid vector Y (4 bytes, signed)
        int gridVectorY = reader.ReadInt32BigEndian();

        // 7.4.5.6 - Region vector X (2 bytes)
        ushort regionVectorX = reader.ReadUInt16BigEndian();

        // 7.4.5.7 - Region vector Y (2 bytes)
        ushort regionVectorY = reader.ReadUInt16BigEndian();

        // Read adaptive template pixels if needed
        (int dx, int dy)[] adaptivePixels;
        if (!useMmr && hdTemplate == 0)
        {
            // 4 AT pixels for template 0
            adaptivePixels = new (int, int)[4];
            adaptivePixels[0] = ((sbyte)reader.ReadByte(), (sbyte)reader.ReadByte());
            adaptivePixels[1] = ((sbyte)reader.ReadByte(), (sbyte)reader.ReadByte());
            adaptivePixels[2] = ((sbyte)reader.ReadByte(), (sbyte)reader.ReadByte());
            adaptivePixels[3] = ((sbyte)reader.ReadByte(), (sbyte)reader.ReadByte());
        }
        else
        {
            adaptivePixels = [];
        }

        // Find pattern dictionary from referred segments
        PatternDictionary? patternDict = null;
        foreach (uint refSegNum in header.ReferredToSegments)
        {
            if (_segments.TryGetValue(refSegNum, out object? refSeg) && refSeg is PatternDictionary pd)
            {
                patternDict = pd;
                break;
            }
        }

        if (patternDict == null)
            throw new Jbig2DataException("No pattern dictionary referenced by halftone region");

        // Create parameters
        var htParams = new HalftoneRegionParams
        {
            UseMmr = useMmr,
            Template = hdTemplate,
            EnableSkip = enableSkip,
            CombinationOp = (CombinationOperator)hCombOp,
            DefaultPixel = defaultPixel,
            GridWidth = (int)gridWidth,
            GridHeight = (int)gridHeight,
            GridVectorX = gridVectorX,
            GridVectorY = gridVectorY,
            RegionVectorX = regionVectorX,
            RegionVectorY = regionVectorY,
            AdaptivePixels = adaptivePixels
        };

        // Decode the halftone region
        int dataOffset = reader.BytePosition;
        int dataLength = data.Length - dataOffset;

        if (dataLength <= 0)
            throw new Jbig2DataException("No data in halftone region segment");

        var htDecoder = new HalftoneRegionDecoder(data, dataOffset, dataLength, htParams, patternDict, _options);
        Bitmap regionBitmap = htDecoder.Decode((int)width, (int)height);

        if (immediate)
        {
            // Composite onto page
            if (_pages.TryGetValue(header.PageAssociation, out Bitmap? pageBitmap))
            {
                if (x > int.MaxValue || y > int.MaxValue)
                    throw new Jbig2DataException($"Region position overflow: ({x}, {y})");

                var op = (CombinationOperator)(combOp & 0x07);
                pageBitmap.Blit(regionBitmap, (int)x, (int)y, op);
            }
        }
        else
        {
            _segments[header.SegmentNumber] = regionBitmap;
        }
    }

    private void ProcessGenericRefinementRegion(SegmentHeader header, byte[] data, bool immediate)
    {
        // T.88 7.4.7 - Generic refinement region segment
        if (data.Length < 18)
            throw new Jbig2DataException("Generic refinement region segment too short");

        var reader = new BitReader(data);

        // Region segment information field (T.88 7.4.1)
        uint width = reader.ReadUInt32BigEndian();
        uint height = reader.ReadUInt32BigEndian();
        uint x = reader.ReadUInt32BigEndian();
        uint y = reader.ReadUInt32BigEndian();
        byte combOp = reader.ReadByte();

        _options.ValidateDimensions((int)width, (int)height, "Generic refinement region");

        // 7.4.7.1 - Generic refinement region segment flags (1 byte)
        byte flags = reader.ReadByte();
        int grTemplate = flags & 0x01;           // GRTEMPLATE: 0 or 1
        bool typicalPrediction = (flags & 0x02) != 0;  // TPGDON

        // 7.4.7.2 - Adaptive template pixels (only if GRTEMPLATE == 0)
        (int dx, int dy)[] adaptivePixels;
        if (grTemplate == 0)
        {
            // 2 adaptive pixels for template 0
            adaptivePixels = new (int, int)[2];
            adaptivePixels[0] = ((sbyte)reader.ReadByte(), (sbyte)reader.ReadByte());
            adaptivePixels[1] = ((sbyte)reader.ReadByte(), (sbyte)reader.ReadByte());
        }
        else
        {
            adaptivePixels = [];
        }

        // Find reference bitmap from referred segments
        // T.88 7.4.7 says the referred segment must be an intermediate region
        Bitmap? reference = null;
        foreach (uint refSegNum in header.ReferredToSegments)
        {
            if (_segments.TryGetValue(refSegNum, out object? refSeg) && refSeg is Bitmap refBitmap)
            {
                reference = refBitmap;
                break;
            }
        }

        if (reference == null)
            throw new Jbig2DataException("No reference bitmap found for generic refinement region");

        // Note: TPGDON (typical prediction) is not commonly used in generic refinement regions
        // The spec says it's only defined for regions where the reference and decoded bitmaps
        // have the same dimensions. For simplicity, we don't implement it here.
        if (typicalPrediction)
        {
            // TPGDON requires that decoded region and reference have same dimensions
            if (width != reference.Width || height != reference.Height)
                throw new Jbig2UnsupportedException("TPGDON in generic refinement region requires matching dimensions");
            // For now, we don't implement TPGDON optimization - just decode normally
        }

        // Create arithmetic decoder from remaining data
        int dataOffset = reader.BytePosition;
        int dataLength = data.Length - dataOffset;

        if (dataLength <= 0)
            throw new Jbig2DataException("No data in generic refinement region segment");

        var arithmeticDecoder = new ArithmeticDecoder(data, dataOffset, dataLength, this);

        // Reference offset is (0, 0) for generic refinement regions
        // T.88 says the reference bitmap is positioned with its top-left corner at (0, 0)
        // relative to the decoded region
        var refinementDecoder = new RefinementRegionDecoder(
            arithmeticDecoder,
            reference,
            refDx: 0,
            refDy: 0,
            grTemplate,
            adaptivePixels,
            _options);

        Bitmap regionBitmap = refinementDecoder.Decode((int)width, (int)height);

        if (immediate)
        {
            // Composite onto page
            if (_pages.TryGetValue(header.PageAssociation, out Bitmap? pageBitmap))
            {
                if (x > int.MaxValue || y > int.MaxValue)
                    throw new Jbig2DataException($"Region position overflow: ({x}, {y})");

                var op = (CombinationOperator)(combOp & 0x07);
                pageBitmap.Blit(regionBitmap, (int)x, (int)y, op);
            }
        }
        else
        {
            _segments[header.SegmentNumber] = regionBitmap;
        }
    }
}
