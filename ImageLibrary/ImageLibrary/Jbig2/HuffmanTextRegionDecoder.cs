using System;

namespace ImageLibrary.Jbig2;

/// <summary>
/// Decodes Huffman-coded text region segments.
/// T.88 Section 6.4 and 7.4.3.
/// </summary>
internal sealed class HuffmanTextRegionDecoder
{
    private readonly HuffmanDecoder _decoder;
    private readonly TextRegionParams _params;
    private readonly SymbolDictionary[] _symbolDictionaries;
    private readonly int _totalSymbols;
    private readonly Jbig2DecoderOptions _options;

    // Huffman tables for this region
    private readonly HuffmanTable _tableFS;
    private readonly HuffmanTable _tableDS;
    private readonly HuffmanTable _tableDT;
    private readonly HuffmanTable _tableRDW;
    private readonly HuffmanTable _tableRDH;
    private readonly HuffmanTable _tableRDX;
    private readonly HuffmanTable _tableRDY;
    private readonly HuffmanTable _tableRSIZE;
    private HuffmanTable? _tableSymbolID;

    /// <summary>
    /// Creates a Huffman text region decoder.
    /// </summary>
    /// <param name="decoder">The Huffman decoder for the segment data</param>
    /// <param name="parameters">Text region parameters</param>
    /// <param name="symbolDictionaries">Symbol dictionaries from referred segments</param>
    /// <param name="options">Decoder options</param>
    /// <param name="customTables">Optional array of custom Huffman tables from referred Table segments</param>
    public HuffmanTextRegionDecoder(
        HuffmanDecoder decoder,
        TextRegionParams parameters,
        SymbolDictionary[] symbolDictionaries,
        Jbig2DecoderOptions? options = null,
        HuffmanTable[]? customTables = null)
    {
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _params = parameters ?? throw new ArgumentNullException(nameof(parameters));
        _symbolDictionaries = symbolDictionaries ?? throw new ArgumentNullException(nameof(symbolDictionaries));
        _options = options ?? Jbig2DecoderOptions.Default;

        if (!_params.UseHuffman)
            throw new ArgumentException("Parameters must have UseHuffman=true", nameof(parameters));

        // Count total symbols across all dictionaries
        _totalSymbols = 0;
        foreach (var dict in _symbolDictionaries)
            _totalSymbols += dict?.Count ?? 0;

        if (_totalSymbols == 0)
            throw new Jbig2DataException("No symbols available for text region decoding");

        // Custom tables are used in order of reference
        var customTableIndex = 0;

        // Select Huffman tables based on parameters (7.4.3.1.6)
        _tableFS = SelectTable(_params.HuffmanFS, ref customTableIndex, customTables,
            StandardHuffmanTables.TableF,  // 0 = B.6
            StandardHuffmanTables.TableG,  // 1 = B.7
            null);                         // 2 = invalid

        _tableDS = SelectTable(_params.HuffmanDS, ref customTableIndex, customTables,
            StandardHuffmanTables.TableH,  // 0 = B.8
            StandardHuffmanTables.TableI,  // 1 = B.9
            StandardHuffmanTables.TableJ); // 2 = B.10

        _tableDT = SelectTable(_params.HuffmanDT, ref customTableIndex, customTables,
            StandardHuffmanTables.TableK,  // 0 = B.11
            StandardHuffmanTables.TableL,  // 1 = B.12
            StandardHuffmanTables.TableM); // 2 = B.13

        _tableRDW = SelectTable(_params.HuffmanRDW, ref customTableIndex, customTables,
            StandardHuffmanTables.TableN,  // 0 = B.14
            StandardHuffmanTables.TableO,  // 1 = B.15
            null);                         // 2 = invalid

        _tableRDH = SelectTable(_params.HuffmanRDH, ref customTableIndex, customTables,
            StandardHuffmanTables.TableN,  // 0 = B.14
            StandardHuffmanTables.TableO,  // 1 = B.15
            null);                         // 2 = invalid

        _tableRDX = SelectTable(_params.HuffmanRDX, ref customTableIndex, customTables,
            StandardHuffmanTables.TableN,  // 0 = B.14
            StandardHuffmanTables.TableO,  // 1 = B.15
            null);                         // 2 = invalid

        _tableRDY = SelectTable(_params.HuffmanRDY, ref customTableIndex, customTables,
            StandardHuffmanTables.TableN,  // 0 = B.14
            StandardHuffmanTables.TableO,  // 1 = B.15
            null);                         // 2 = invalid

        _tableRSIZE = SelectTable(_params.HuffmanRSIZE, ref customTableIndex, customTables,
            StandardHuffmanTables.TableA,  // 0 = B.1
            null,                          // 1 = invalid
            null);                         // 2 = invalid

        // Build the symbol ID Huffman table (7.4.3.1.7)
        BuildSymbolIDTable();
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
    /// Build the symbol ID Huffman table from run-length coded lengths.
    /// T.88 Section 7.4.3.1.7 and Annex B.
    /// </summary>
    private void BuildSymbolIDTable()
    {
        // Step 1: Read 35 4-bit values for the runcode table
        var runcodeLengths = new HuffmanLine[35];
        for (var i = 0; i < 35; i++)
        {
            int prefLen = _decoder.ReadBits(4);
            runcodeLengths[i] = new HuffmanLine(prefLen, 0, i);
        }

        // Build the runcode table
        var runcodeParams = new HuffmanParams(false, runcodeLengths);
        var runcodeTable = HuffmanTable.Build(runcodeParams);

        // Step 2: Decode symbol code lengths using run-length coding
        var symcodeLengths = new HuffmanLine[_totalSymbols];
        var index = 0;

        while (index < _totalSymbols)
        {
            int code = _decoder.Decode(runcodeTable);
            if (code == HuffmanDecoder.OOB)
                throw new Jbig2DataException("Unexpected OOB decoding symbol ID table");

            if (code < 0 || code >= 35)
                throw new Jbig2DataException($"Symbol ID table runcode out of range: {code}");

            int len, range;
            if (code < 32)
            {
                len = code;
                range = 1;
            }
            else
            {
                // Run-length codes
                if (code == 32)
                {
                    if (index < 1)
                        throw new Jbig2DataException("Run-length code 32 with no antecedent");
                    len = symcodeLengths[index - 1].PrefixLength;
                    range = _decoder.ReadBits(2) + 3;
                }
                else if (code == 33)
                {
                    len = 0;
                    range = _decoder.ReadBits(3) + 3;
                }
                else // code == 34
                {
                    len = 0;
                    range = _decoder.ReadBits(7) + 11;
                }
            }

            // Apply the run
            if (index + range > _totalSymbols)
            {
                range = _totalSymbols - index;
            }

            for (var r = 0; r < range; r++)
            {
                symcodeLengths[index + r] = new HuffmanLine(len, 0, index + r);
            }
            index += range;
        }

        // Step 3: Build the symbol ID table
        var symcodeParams = new HuffmanParams(false, symcodeLengths);
        _tableSymbolID = HuffmanTable.Build(symcodeParams);

        // Skip to byte boundary after symbol ID table
        _decoder.SkipToByteAlign();
    }

    /// <summary>
    /// Decodes a text region.
    /// T.88 Section 6.4.5.
    /// </summary>
    public Bitmap Decode(int width, int height)
    {
        _options.ValidateDimensions(width, height, "Text region");

        var bitmap = new Bitmap(width, height, _options);

        // Default pixel fill
        if (_params.DefaultPixel != 0)
            bitmap.Fill(1);

        // 6.4.5 (1) - Decode initial STRIPT
        int stript = _decoder.Decode(_tableDT);
        if (stript == HuffmanDecoder.OOB)
            throw new Jbig2DataException("Unexpected OOB in initial strip T");
        stript *= -_params.StripSize;

        var firstS = 0;
        var instancesDecoded = 0;

        var loopIterations = 0;

        while (instancesDecoded < _params.NumInstances)
        {
            if (++loopIterations > _options.MaxLoopIterations)
                throw new Jbig2ResourceException($"Huffman text region decode iteration limit exceeded ({_options.MaxLoopIterations})");

            // 6.4.6 - Decode strip delta T (DT)
            int dt = _decoder.Decode(_tableDT);
            if (dt == HuffmanDecoder.OOB)
                throw new Jbig2DataException("Unexpected OOB in strip delta T");
            dt *= _params.StripSize;
            stript += dt;

            // 6.4.7 - Decode first S coordinate
            int dfs = _decoder.Decode(_tableFS);
            if (dfs == HuffmanDecoder.OOB)
                throw new Jbig2DataException("Unexpected OOB in first S");
            firstS += dfs;
            int curS = firstS;

            var firstSymbol = true;
            var innerIterations = 0;

            // Decode symbols in this strip
            while (true)
            {
                if (++innerIterations > _options.MaxLoopIterations)
                    throw new Jbig2ResourceException($"Huffman text region strip iteration limit exceeded ({_options.MaxLoopIterations})");

                if (!firstSymbol)
                {
                    // 6.4.8 - Decode delta S
                    int ids = _decoder.Decode(_tableDS);
                    if (ids == HuffmanDecoder.OOB)
                    {
                        break; // End of strip
                    }
                    curS += ids + _params.StripDeltaOffset;
                }
                firstSymbol = false;

                // 6.4.9 - Instance T within strip
                int curt;
                if (_params.StripSize == 1)
                {
                    curt = 0;
                }
                else
                {
                    curt = _decoder.ReadBits(_params.LogStripSize);
                }

                int t = stript + curt;

                // 6.4.10 - Decode symbol ID
                int symbolId = _decoder.Decode(_tableSymbolID!);
                if (symbolId == HuffmanDecoder.OOB || symbolId < 0 || symbolId >= _totalSymbols)
                    throw new Jbig2DataException($"Invalid symbol ID: {symbolId}");

                // Get symbol bitmap
                Bitmap? symbolBitmap = GetSymbol(symbolId);
                if (symbolBitmap == null)
                    throw new Jbig2DataException($"Symbol not found: {symbolId}");

                // 6.4.11 - Check for refinement
                if (_params.UseRefinement)
                {
                    int ri = _decoder.ReadBits(1);
                    if (ri != 0)
                    {
                        // Apply refinement to this instance
                        symbolBitmap = DecodeRefinedInstance(symbolBitmap);
                    }
                }

                // Adjust curS for reference corner (6.4.5 step 3c.vi)
                if (!_params.Transposed && (int)_params.ReferenceCorner > 1)
                {
                    curS += symbolBitmap.Width - 1;
                }
                else if (_params.Transposed && ((int)_params.ReferenceCorner & 1) == 0)
                {
                    curS += symbolBitmap.Height - 1;
                }

                // 6.4.12 - Place symbol on bitmap
                PlaceSymbol(bitmap, symbolBitmap, curS, t);

                // Advance curS for next symbol
                if (!_params.Transposed && (int)_params.ReferenceCorner < 2)
                {
                    curS += symbolBitmap.Width - 1;
                }
                else if (_params.Transposed && ((int)_params.ReferenceCorner & 1) != 0)
                {
                    curS += symbolBitmap.Height - 1;
                }

                instancesDecoded++;
                if (instancesDecoded >= _params.NumInstances)
                    break;
            }
        }

        return bitmap;
    }

    private Bitmap? GetSymbol(int symbolId)
    {
        int id = symbolId;
        foreach (var dict in _symbolDictionaries)
        {
            if (dict == null) continue;
            if (id < dict.Count)
                return dict[id];
            id -= dict.Count;
        }
        return null;
    }

    private Bitmap DecodeRefinedInstance(Bitmap reference)
    {
        // Decode refinement parameters using Huffman (T.88 6.4.11)
        int rdw = _decoder.Decode(_tableRDW);
        int rdh = _decoder.Decode(_tableRDH);
        int rdx = _decoder.Decode(_tableRDX);
        int rdy = _decoder.Decode(_tableRDY);

        if (rdw == HuffmanDecoder.OOB || rdh == HuffmanDecoder.OOB ||
            rdx == HuffmanDecoder.OOB || rdy == HuffmanDecoder.OOB)
            throw new Jbig2DataException("Unexpected OOB in refinement parameters");

        int bmsize = _decoder.Decode(_tableRSIZE);
        if (bmsize == HuffmanDecoder.OOB)
            throw new Jbig2DataException("Unexpected OOB in refinement bitmap size");

        // T.88 6.4.11.2 - Calculate IBO (instance bitmap dimensions)
        int iboWidth = reference.Width + rdw;
        int iboHeight = reference.Height + rdh;

        if (iboWidth <= 0 || iboHeight <= 0)
            throw new Jbig2DataException($"Invalid refined symbol dimensions: {iboWidth}x{iboHeight}");

        // Skip to byte boundary before bitmap data
        _decoder.SkipToByteAlign();

        // Get the arithmetic-coded refinement data
        int dataOffset = _decoder.BytePosition;
        int dataLength = bmsize > 0 ? bmsize : _decoder.RemainingBytes;

        if (dataLength <= 0)
            throw new Jbig2DataException("No data for refinement bitmap");

        // Create arithmetic decoder for the refinement data
        var arithmeticDecoder = new ArithmeticDecoder(_decoder.GetData(), dataOffset, dataLength);

        // Decode refinement region (T.88 6.3)
        // The reference position is adjusted by RDX, RDY
        var refinementDecoder = new RefinementRegionDecoder(
            arithmeticDecoder,
            reference,
            rdx, rdy,
            _params.RefinementTemplate,
            _params.RefinementAdaptivePixels,
            _options);

        Bitmap result = refinementDecoder.Decode(iboWidth, iboHeight);

        // Advance past the refinement data
        if (bmsize > 0)
            _decoder.Advance(bmsize);

        return result;
    }

    private void PlaceSymbol(Bitmap target, Bitmap symbol, int s, int t)
    {
        int x, y;

        if (_params.Transposed)
        {
            x = t;
            y = s;
        }
        else
        {
            x = s;
            y = t;
        }

        // Adjust for reference corner
        switch (_params.ReferenceCorner)
        {
            case ReferenceCorner.BottomLeft:
                y -= symbol.Height - 1;
                break;
            case ReferenceCorner.TopRight:
                x -= symbol.Width - 1;
                break;
            case ReferenceCorner.BottomRight:
                x -= symbol.Width - 1;
                y -= symbol.Height - 1;
                break;
            case ReferenceCorner.TopLeft:
            default:
                // No adjustment needed
                break;
        }

        target.Blit(symbol, x, y, _params.CombinationOp);
    }
}
