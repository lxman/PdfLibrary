using System;

namespace ImageLibrary.Jbig2;

/// <summary>
/// Decodes Huffman-coded symbol dictionary segments.
/// T.88 Section 6.5 and 7.4.2.
/// </summary>
internal sealed class HuffmanSymbolDictionaryDecoder
{
    private readonly HuffmanDecoder _decoder;
    private readonly SymbolDictionaryParams _params;
    private readonly SymbolDictionary _inputSymbols;
    private readonly Jbig2DecoderOptions _options;

    // Huffman tables for this dictionary
    private readonly HuffmanTable _tableDH;
    private readonly HuffmanTable _tableDW;
    private readonly HuffmanTable _tableBMSIZE;
    private readonly HuffmanTable _tableAGGINST;

    /// <summary>
    /// Creates a Huffman symbol dictionary decoder.
    /// </summary>
    /// <param name="decoder">The Huffman decoder for the segment data</param>
    /// <param name="parameters">Symbol dictionary parameters</param>
    /// <param name="inputSymbols">Input symbols from referred segments</param>
    /// <param name="options">Decoder options</param>
    /// <param name="customTables">Optional array of custom Huffman tables from referred Table segments</param>
    public HuffmanSymbolDictionaryDecoder(
        HuffmanDecoder decoder,
        SymbolDictionaryParams parameters,
        SymbolDictionary inputSymbols,
        Jbig2DecoderOptions? options = null,
        HuffmanTable[]? customTables = null)
    {
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _params = parameters ?? throw new ArgumentNullException(nameof(parameters));
        _inputSymbols = inputSymbols ?? new SymbolDictionary();
        _options = options ?? Jbig2DecoderOptions.Default;

        if (!_params.UseHuffman)
            throw new ArgumentException("Parameters must have UseHuffman=true", nameof(parameters));

        // Custom tables are used in order of reference
        var customTableIndex = 0;

        // Select Huffman tables based on parameters (7.4.2.1.1)
        // DH: 0 = B.4, 1 = B.5, 3 = custom
        _tableDH = SelectTable(_params.HuffmanDH, ref customTableIndex, customTables,
            StandardHuffmanTables.TableD,  // 0 = B.4
            StandardHuffmanTables.TableE,  // 1 = B.5
            null);                         // 2 = invalid

        // DW: 0 = B.2, 1 = B.3, 3 = custom
        _tableDW = SelectTable(_params.HuffmanDW, ref customTableIndex, customTables,
            StandardHuffmanTables.TableB,  // 0 = B.2
            StandardHuffmanTables.TableC,  // 1 = B.3
            null);                         // 2 = invalid

        // BMSIZE: 0 = B.1, 1 = custom
        _tableBMSIZE = SelectTable(_params.HuffmanBMSIZE, ref customTableIndex, customTables,
            StandardHuffmanTables.TableA,  // 0 = B.1
            null, null);                   // 1-2 = invalid

        // AGGINST: 0 = B.1, 1 = custom (only used if UseRefinementAgg)
        if (_params.UseRefinementAgg)
        {
            _tableAGGINST = SelectTable(_params.HuffmanAGGINST, ref customTableIndex, customTables,
                StandardHuffmanTables.TableA,  // 0 = B.1
                null, null);                   // 1-2 = invalid
        }
        else
        {
            _tableAGGINST = StandardHuffmanTables.TableA; // Not used, but avoid null
        }
    }

    private static HuffmanTable SelectTable(int selector, ref int customTableIndex,
        HuffmanTable[]? customTables, HuffmanTable? table0, HuffmanTable? table1, HuffmanTable? table2)
    {
        if (selector == 3)
        {
            // Custom table - use the next available custom table from referred segments
            if (customTables == null || customTableIndex >= customTables.Length)
                throw new Jbig2DataException("Custom Huffman table referenced but not available in referred segments");
            return customTables[customTableIndex++];
        }

        return selector switch
        {
            0 => table0 ?? throw new Jbig2DataException($"Invalid Huffman table selector: {selector}"),
            1 => table1 ?? throw new Jbig2DataException($"Invalid Huffman table selector: {selector}"),
            2 => table2 ?? throw new Jbig2DataException($"Invalid Huffman table selector: {selector}"),
            _ => throw new Jbig2DataException($"Invalid Huffman table selector: {selector}")
        };
    }

    /// <summary>
    /// Decodes the symbol dictionary and returns the exported symbols.
    /// T.88 Section 6.5.5.
    /// </summary>
    public SymbolDictionary Decode()
    {
        var newSymbols = new SymbolDictionary();
        var heightClassHeight = 0;
        var symbolsDecoded = 0;

        // For SDHUFF && !SDREFAGG, we need to track symbol widths per height class
        // to slice them from the collective bitmap later
        int[]? symbolWidths = !_params.UseRefinementAgg ? new int[_params.NumNewSymbols] : null;

        var loopIterations = 0;

        // Decode height classes (6.5.5)
        while (symbolsDecoded < _params.NumNewSymbols)
        {
            if (++loopIterations > _options.MaxLoopIterations)
                throw new Jbig2ResourceException($"Huffman symbol dictionary decode iteration limit exceeded ({_options.MaxLoopIterations})");

            // 6.5.6 - Decode delta height (HCDH)
            int deltaHeight = _decoder.Decode(_tableDH);
            if (deltaHeight == HuffmanDecoder.OOB)
                throw new Jbig2DataException("Unexpected OOB in symbol dictionary height decode");

            heightClassHeight += deltaHeight;

            if (heightClassHeight < 0)
                throw new Jbig2DataException($"Invalid symbol height: {heightClassHeight}");

            var symbolWidth = 0;
            var totalWidthThisClass = 0;
            int heightClassFirstSymbol = symbolsDecoded;

            // 6.5.7 - Decode symbols in this height class
            // The height class is always terminated by OOB, so loop until we get it
            var innerIterations = 0;
            while (true)
            {
                if (++innerIterations > _options.MaxLoopIterations)
                    throw new Jbig2ResourceException($"Huffman symbol dictionary height class iteration limit exceeded ({_options.MaxLoopIterations})");

                // Decode delta width (DW)
                int deltaWidth = _decoder.Decode(_tableDW);
                if (deltaWidth == HuffmanDecoder.OOB) // End of height class
                {
                    break;
                }

                // Safety check - shouldn't happen if encoder is correct
                if (symbolsDecoded >= _params.NumNewSymbols)
                    throw new Jbig2DataException($"More symbols in height class than expected: decoded {symbolsDecoded}, expected {_params.NumNewSymbols}");

                symbolWidth += deltaWidth;
                if (symbolWidth < 0)
                    throw new Jbig2DataException($"Invalid symbol width: {symbolWidth}");

                // Validate dimensions
                _options.ValidateDimensions(symbolWidth, heightClassHeight, "Symbol");

                if (_params.UseRefinementAgg)
                {
                    // 6.5.8.2 - Refinement/aggregate coding
                    Bitmap symbolBitmap = DecodeRefinedSymbol(symbolWidth, heightClassHeight, newSymbols);
                    newSymbols.Add(symbolBitmap);
                }
                else
                {
                    // 6.5.8.1 - Direct coding
                    // Store width for later slicing from collective bitmap
                    symbolWidths![symbolsDecoded] = symbolWidth;
                    totalWidthThisClass += symbolWidth;
                }

                symbolsDecoded++;
            }

            // 6.5.9 - Decode collective bitmap for this height class (when SDHUFF && !SDREFAGG)
            if (!_params.UseRefinementAgg && symbolsDecoded > heightClassFirstSymbol)
            {
                DecodeCollectiveBitmap(
                    newSymbols, symbolWidths!,
                    heightClassFirstSymbol, symbolsDecoded,
                    totalWidthThisClass, heightClassHeight);
            }
        }

        // 6.5.10 - Decode export flags
        return DecodeExportedSymbols(newSymbols);
    }

    /// <summary>
    /// Decodes the collective bitmap for a height class and slices it into individual symbols.
    /// T.88 Section 6.5.9.
    /// </summary>
    private void DecodeCollectiveBitmap(
        SymbolDictionary newSymbols,
        int[] symbolWidths,
        int firstSymbol,
        int lastSymbol,
        int totalWidth,
        int height)
    {
        // Decode BMSIZE
        int bmsize = _decoder.Decode(_tableBMSIZE);
        if (bmsize == HuffmanDecoder.OOB)
            throw new Jbig2DataException("Unexpected OOB in collective bitmap size");

        // Skip to byte boundary before bitmap data
        _decoder.SkipToByteAlign();

        // Create collective bitmap
        var collectiveBitmap = new Bitmap(totalWidth, height, _options);

        if (bmsize == 0)
        {
            // Uncompressed bitmap - read raw bytes
            int stride = (totalWidth + 7) / 8;
            int expectedBytes = stride * height;

            if (_decoder.RemainingBytes < expectedBytes)
                throw new Jbig2DataException($"Not enough data for uncompressed collective bitmap: need {expectedBytes}, have {_decoder.RemainingBytes}");

            // Read the bitmap data row by row
            for (var y = 0; y < height; y++)
            {
                for (var byteX = 0; byteX < stride; byteX++)
                {
                    int b = _decoder.ReadBits(8);
                    for (var bit = 0; bit < 8 && (byteX * 8 + bit) < totalWidth; bit++)
                    {
                        int pixelX = byteX * 8 + bit;
                        int pixel = (b >> (7 - bit)) & 1;
                        collectiveBitmap.SetPixel(pixelX, y, pixel);
                    }
                }
            }
        }
        else
        {
            // MMR-compressed bitmap
            if (_decoder.RemainingBytes < bmsize)
                throw new Jbig2DataException($"Not enough data for MMR collective bitmap: need {bmsize}, have {_decoder.RemainingBytes}");

            // Get the raw bytes for MMR decoding
            int currentPos = _decoder.BytePosition;
            var mmrDecoder = new MmrDecoder(_decoder.GetData(), currentPos, bmsize, totalWidth, height);
            collectiveBitmap = mmrDecoder.Decode();
        }

        // Advance past the bitmap data
        if (bmsize > 0)
            _decoder.Advance(bmsize);

        // Slice the collective bitmap into individual symbols
        var x = 0;
        for (int i = firstSymbol; i < lastSymbol; i++)
        {
            int symbolWidth = symbolWidths[i];
            var symbolBitmap = new Bitmap(symbolWidth, height, _options);

            // Copy pixels from collective bitmap
            for (var sy = 0; sy < height; sy++)
            {
                for (var sx = 0; sx < symbolWidth; sx++)
                {
                    int pixel = collectiveBitmap.GetPixel(x + sx, sy);
                    symbolBitmap.SetPixel(sx, sy, pixel);
                }
            }

            newSymbols.Add(symbolBitmap);
            x += symbolWidth;
        }
    }

    private Bitmap DecodeRefinedSymbol(int width, int height, SymbolDictionary newSymbols)
    {
        // Decode aggregate instance count
        int aggregateCount = _decoder.Decode(_tableAGGINST);
        if (aggregateCount == HuffmanDecoder.OOB)
            throw new Jbig2DataException("Unexpected OOB in aggregate instance count");

        if (aggregateCount == 1)
        {
            // Single symbol refinement
            return DecodeSingleRefinement(width, height, newSymbols);
        }

        // Multiple symbol aggregation
        throw new Jbig2UnsupportedException("Multi-symbol aggregation not yet implemented");
    }

    private Bitmap DecodeSingleRefinement(int width, int height, SymbolDictionary newSymbols)
    {
        // Decode symbol ID using Huffman
        // T.88 6.5.8.2.2 (3): SBNUMSYMS = SDNUMINSYMS + SDNUMNEWSYMS (total symbols)
        int totalSymbols = _inputSymbols.Count + _params.NumNewSymbols;
        int symbolId = DecodeSymbolId(totalSymbols);

        // Get reference symbol
        Bitmap? refSymbol;
        if (symbolId < _inputSymbols.Count)
            refSymbol = _inputSymbols[symbolId];
        else
            refSymbol = newSymbols.GetSymbol(symbolId - _inputSymbols.Count);

        if (refSymbol == null)
            throw new Jbig2DataException($"Invalid symbol reference: {symbolId}");

        // Decode refinement deltas using Table B.15
        int rdx = _decoder.Decode(StandardHuffmanTables.TableO);
        int rdy = _decoder.Decode(StandardHuffmanTables.TableO);

        if (rdx == HuffmanDecoder.OOB || rdy == HuffmanDecoder.OOB)
            throw new Jbig2DataException("Unexpected OOB in refinement position");

        // Decode RSIZE (bitmap size) using Table B.1
        int rsize = _decoder.Decode(StandardHuffmanTables.TableA);
        if (rsize == HuffmanDecoder.OOB)
            throw new Jbig2DataException("Unexpected OOB in refinement bitmap size");

        // Skip to byte boundary before arithmetic-coded data
        _decoder.SkipToByteAlign();

        // The refinement bitmap data is arithmetic-coded
        int dataOffset = _decoder.BytePosition;
        int dataLength = rsize > 0 ? rsize : (_decoder.RemainingBytes);

        if (dataLength <= 0)
            throw new Jbig2DataException("No data for refinement bitmap");

        // Create arithmetic decoder for the refinement data
        var arithmeticDecoder = new ArithmeticDecoder(_decoder.GetData(), dataOffset, dataLength);

        // Decode refinement region
        var refinementDecoder = new RefinementRegionDecoder(
            arithmeticDecoder,
            refSymbol,
            rdx, rdy,
            _params.RefinementTemplate,
            _params.RefinementAdaptivePixels,
            _options);

        Bitmap result = refinementDecoder.Decode(width, height);

        // Advance past the refinement data
        if (rsize > 0)
            _decoder.Advance(rsize);

        return result;
    }

    private int DecodeSymbolId(int numSymbols)
    {
        // For Huffman coding, symbol IDs are coded directly using a simple code
        // based on the number of bits needed to represent the symbol count
        var bits = 0;
        int n = numSymbols - 1;
        while (n > 0) { bits++; n >>= 1; }

        int id = _decoder.ReadBits(bits);
        return id;
    }

    private SymbolDictionary DecodeExportedSymbols(SymbolDictionary newSymbols)
    {
        // T.88 Section 6.5.10 - Export symbol table
        int totalSymbols = _inputSymbols.Count + newSymbols.Count;
        var exportFlags = new bool[totalSymbols];

        var currentExport = 0; // Start with non-export
        var i = 0;

        // Use Table B.1 for export run lengths
        HuffmanTable tableExport = StandardHuffmanTables.TableA;
        var emptyRuns = 0;

        while (i < totalSymbols)
        {
            // Decode run length
            int runLength = _decoder.Decode(tableExport);
            if (runLength == HuffmanDecoder.OOB)
                throw new Jbig2DataException("Unexpected OOB in export flags");

            // Prevent infinite loops
            if (runLength <= 0)
            {
                emptyRuns++;
                if (emptyRuns > 1000)
                    throw new Jbig2DataException("Too many empty export runs");
            }
            else
            {
                emptyRuns = 0;
            }

            // Set flags for this run
            bool exporting = (currentExport == 1);
            for (var j = 0; j < runLength && i < totalSymbols; j++, i++)
            {
                exportFlags[i] = exporting;
            }

            // Toggle export state
            currentExport = 1 - currentExport;
        }

        // Build exported dictionary
        var result = new SymbolDictionary();
        for (i = 0; i < totalSymbols; i++)
        {
            if (exportFlags[i])
            {
                if (i < _inputSymbols.Count)
                    result.Add(_inputSymbols[i]);
                else
                    result.Add(newSymbols[i - _inputSymbols.Count]);
            }
        }

        // Validate export count
        if (result.Count != _params.NumExportedSymbols)
            throw new Jbig2DataException($"Export count mismatch: expected {_params.NumExportedSymbols}, got {result.Count}");

        return result;
    }
}
