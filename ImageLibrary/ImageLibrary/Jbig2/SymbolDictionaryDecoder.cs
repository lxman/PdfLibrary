using System;

namespace ImageLibrary.Jbig2;

/// <summary>
/// Decodes symbol dictionary segments.
/// T.88 Section 6.5 and 7.4.2.
/// </summary>
internal sealed class SymbolDictionaryDecoder
{
    private readonly ArithmeticDecoder _decoder;
    private readonly SymbolDictionaryParams _params;
    private readonly SymbolDictionary _inputSymbols;
    private readonly Jbig2DecoderOptions _options;

    // Arithmetic coding contexts
    private readonly ArithmeticDecoder.Context[] _iaDhContexts;  // Delta height
    private readonly ArithmeticDecoder.Context[] _iaDwContexts;  // Delta width
    private readonly ArithmeticDecoder.Context[] _iaExContexts;  // Export flags
    private readonly ArithmeticDecoder.Context[] _iaAiContexts;  // Aggregate instance count

    // Shared contexts for refinement/aggregation text region operations
    // These are shared between single refinement (6.5.8.2.2) and multi-symbol aggregation (6.5.8.2.3)
    private readonly TextRegionSharedContexts? _textRegionContexts;

    // Generic region contexts - shared across all symbol bitmaps in this dictionary
    private readonly ArithmeticDecoder.Context[] _gbContexts;

    // Refinement region contexts - shared across all refinement operations (T.88 6.5.5 step 11)
    private readonly ArithmeticDecoder.Context[]? _grContexts;

    public SymbolDictionaryDecoder(
        ArithmeticDecoder decoder,
        SymbolDictionaryParams parameters,
        SymbolDictionary inputSymbols,
        Jbig2DecoderOptions? options = null)
    {
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _params = parameters ?? throw new ArgumentNullException(nameof(parameters));
        _inputSymbols = inputSymbols ?? new SymbolDictionary();
        _options = options ?? Jbig2DecoderOptions.Default;

        if (_params.UseHuffman)
            throw new Jbig2UnsupportedException("Huffman-coded symbol dictionaries not yet implemented");

        // Create arithmetic coding contexts
        _iaDhContexts = ArithmeticDecoder.CreateIntContexts();
        _iaDwContexts = ArithmeticDecoder.CreateIntContexts();
        _iaExContexts = ArithmeticDecoder.CreateIntContexts();
        _iaAiContexts = ArithmeticDecoder.CreateIntContexts();

        // Create shared text region contexts if refinement/aggregation is used
        // These contexts are shared between single refinement and multi-symbol aggregation
        if (_params.UseRefinementAgg)
        {
            _textRegionContexts = new TextRegionSharedContexts(_params.NumInputSymbols + _params.NumNewSymbols);
        }

        // Refinement contexts - shared across all refinement operations (T.88 6.5.5 step 11)
        if (_params.UseRefinementAgg)
        {
            int grContextCount = _params.RefinementTemplate == 0 ? 8192 : 1024; // 13 bits or 10 bits
            _grContexts = new ArithmeticDecoder.Context[grContextCount];
            for (var i = 0; i < grContextCount; i++)
                _grContexts[i] = new ArithmeticDecoder.Context();

            // Link GR contexts to text region shared contexts for multi-agg to use
            // Per T.88 6.5.5 step 11, GR contexts should be shared across all refinement operations
            _textRegionContexts!.GrContexts = _grContexts;
        }

        // Generic region contexts - shared across all symbol bitmaps
        _gbContexts = GenericRegionDecoder.CreateContexts(_params.Template);
    }

    /// <summary>
    /// Decodes the symbol dictionary and returns the exported symbols.
    /// T.88 Section 6.5.5.
    /// </summary>
    public SymbolDictionary Decode()
    {
        // Validate symbol count against limits
        if (_params.NumNewSymbols > _options.MaxSymbols)
            throw new Jbig2ResourceException($"Symbol dictionary size {_params.NumNewSymbols} exceeds limit {_options.MaxSymbols}");

        var newSymbols = new SymbolDictionary();
        var heightClassHeight = 0;
        var symbolsDecoded = 0;
        var loopIterations = 0;

        // Decode height classes
        while (symbolsDecoded < _params.NumNewSymbols)
        {
            if (++loopIterations > _options.MaxLoopIterations)
                throw new Jbig2ResourceException($"Symbol dictionary decode iteration limit exceeded ({_options.MaxLoopIterations})");
            // 6.5.6 - Decode delta height (HCDH)
            int deltaHeight = _decoder.DecodeInt(_iaDhContexts);
            if (deltaHeight == int.MinValue) // OOB
                throw new Jbig2DataException("Unexpected OOB in symbol dictionary height decode");

            heightClassHeight += deltaHeight;

            if (heightClassHeight < 0)
                throw new Jbig2DataException($"Invalid symbol height: {heightClassHeight}");

            var symbolWidth = 0;
            int heightClassFirstSymbol = symbolsDecoded;

            // 6.5.7 - Decode symbols in this height class
            // Height class is terminated by OOB from DW, but we also exit if we've
            // decoded all expected symbols (the outer loop will handle this).
            var innerIterations = 0;
            while (true)
            {
                if (++innerIterations > _options.MaxLoopIterations)
                    throw new Jbig2ResourceException($"Symbol dictionary height class iteration limit exceeded ({_options.MaxLoopIterations})");
                // Check if we've decoded all symbols BEFORE trying to decode DW.
                if (symbolsDecoded >= _params.NumNewSymbols)
                {
                    // Don't decode trailing OOBs - go directly to export flags
                    break;
                }

                // Decode delta width (DW)
                int deltaWidth = _decoder.DecodeInt(_iaDwContexts);
                if (deltaWidth == int.MinValue) // OOB - end of height class
                {
                    break;
                }

                symbolWidth += deltaWidth;
                if (symbolWidth < 0)
                    throw new Jbig2DataException($"Invalid symbol width: {symbolWidth}");

                // Validate dimensions
                _options.ValidateDimensions(symbolWidth, heightClassHeight, "Symbol");

                Bitmap symbolBitmap;

                if (!_params.UseRefinementAgg)
                {
                    // 6.5.8.1 - Direct coding without refinement/aggregation
                    symbolBitmap = DecodeDirectSymbol(symbolWidth, heightClassHeight);
                }
                else
                {
                    // 6.5.8.2 - Refinement/aggregate coding
                    symbolBitmap = DecodeRefinedSymbol(symbolWidth, heightClassHeight, newSymbols);
                }

                newSymbols.Add(symbolBitmap);
                symbolsDecoded++;

                // Don't break here! We must continue to decode the next DW
                // which will return OOB to properly end the height class.
                // The OOB decode consumes bits that affect subsequent decoding.
            }
        }

        // 6.5.10 - Decode export flags
        return DecodeExportedSymbols(newSymbols);
    }

    private Bitmap DecodeDirectSymbol(int width, int height)
    {
        // Use generic region decoder for direct symbol coding
        // Important: reuse the same contexts across all symbols in this dictionary
        var genericDecoder = new GenericRegionDecoder(
            _decoder,
            _params.Template,
            _params.AdaptivePixels,
            typicalPrediction: false,
            _options,
            _gbContexts);

        return genericDecoder.Decode(width, height);
    }

    private Bitmap DecodeRefinedSymbol(int width, int height, SymbolDictionary newSymbols)
    {
        // Decode aggregate instance count (NSYMSDECODED)
        int aggregateCount = _decoder.DecodeInt(_iaAiContexts);
        if (aggregateCount == int.MinValue)
            throw new Jbig2DataException("Unexpected OOB in aggregate instance count");

        if (aggregateCount == 1)
        {
            // Single symbol refinement
            return DecodeSingleRefinement(width, height, newSymbols);
        }

        // Multiple symbol aggregation - T.88 Section 6.5.8.2.3
        // Uses a text region decoder to composite multiple symbols
        return DecodeMultipleAggregation(width, height, aggregateCount, newSymbols);
    }

    private Bitmap DecodeMultipleAggregation(int width, int height, int aggregateCount, SymbolDictionary newSymbols)
    {
        // Build combined symbol dictionary for text region decoder
        // T.88 6.5.8.2.3: SBSYMS = SDINSYMS + new symbols decoded so far
        var combinedSymbols = new SymbolDictionary();
        for (var i = 0; i < _inputSymbols.Count; i++)
        {
            Bitmap? sym = _inputSymbols.GetSymbol(i);
            if (sym != null) combinedSymbols.Add(sym);
        }
        for (var i = 0; i < newSymbols.Count; i++)
        {
            Bitmap? sym = newSymbols.GetSymbol(i);
            if (sym != null) combinedSymbols.Add(sym);
        }

        // Set up text region parameters per T.88 6.5.8.2.3 Table 18
        // jbig2dec sets SBREFINE = 1 for multi-symbol aggregation, so each instance
        // can potentially be refined (though RI=0 typically, meaning no actual refinement)
        //
        // IMPORTANT: The IAID symbol count (SBNUMSYMS) for context size must be the TOTAL
        // number of symbols (input + ALL new symbols to decode), not just symbols decoded so far.
        // This is because jbig2dec calculates SBSYMCODELEN at the start of symbol dictionary
        // decoding based on SDNUMINSYMS + SDNUMNEWSYMS.
        int totalSymbolsForIaid = _params.NumInputSymbols + _params.NumNewSymbols;

        var textParams = new TextRegionParams
        {
            UseHuffman = false,  // SBHUFF = SDHUFF (we're arithmetic only here)
            UseRefinement = true,  // SBREFINE = 1 (per jbig2dec, decode RI for each instance)
            StripSize = 1,  // SBSTRIPS = 1
            ReferenceCorner = ReferenceCorner.TopLeft,  // REFCORNER = TOPLEFT
            Transposed = false,  // TRANSPOSED = 0
            CombinationOp = CombinationOperator.Or,  // SBCOMBOP = OR
            DefaultPixel = 0,  // SBDEFPIXEL = 0
            StripDeltaOffset = 0,  // SBDSOFFSET = 0
            NumInstances = aggregateCount,  // SBNUMINSTANCES = REFAGGNINST
            LogStripSize = 0,
            RefinementTemplate = _params.RefinementTemplate,  // SBRTEMPLATE = SDRTEMPLATE
            RefinementAdaptivePixels = _params.RefinementAdaptivePixels,  // Refinement AT pixels
            TotalSymbolCountOverride = totalSymbolsForIaid  // SBNUMSYMS for IAID context size
        };

        // Create text region decoder with shared contexts
        // This ensures IAID and other contexts are shared with single refinement
        var textDecoder = new TextRegionDecoder(
            _decoder,
            textParams,
            [combinedSymbols],
            _options,
            _textRegionContexts);

        // Decode the text region (this gives us the aggregate bitmap)
        return textDecoder.Decode(width, height);
    }

    private Bitmap DecodeSingleRefinement(int width, int height, SymbolDictionary newSymbols)
    {
        // Decode symbol ID using IAID procedure
        // T.88 6.5.8.2.2 (3): SBNUMSYMS = SDNUMINSYMS + SDNUMNEWSYMS (total symbols, not just decoded so far)
        int sbnumsyms = _params.NumInputSymbols + _params.NumNewSymbols;
        int symbolId = DecodeSymbolId(sbnumsyms);

        // Get reference symbol
        Bitmap? refSymbol;
        if (symbolId < _inputSymbols.Count)
            refSymbol = _inputSymbols[symbolId];
        else
            refSymbol = newSymbols.GetSymbol(symbolId - _inputSymbols.Count);

        if (refSymbol == null)
            throw new Jbig2DataException($"Invalid symbol reference: {symbolId}");

        // Decode refinement deltas using shared text region contexts
        int rdx = _decoder.DecodeInt(_textRegionContexts!.IaRdxContexts);
        int rdy = _decoder.DecodeInt(_textRegionContexts!.IaRdyContexts);

        if (rdx == int.MinValue || rdy == int.MinValue)
            throw new Jbig2DataException("Unexpected OOB in refinement position");

        // Decode refinement region using shared contexts (T.88 6.5.5 step 11)
        var refinementDecoder = new RefinementRegionDecoder(
            _decoder,
            refSymbol,
            rdx, rdy,
            _params.RefinementTemplate,
            _params.RefinementAdaptivePixels,
            _options,
            _grContexts);

        return refinementDecoder.Decode(width, height);
    }

    private int DecodeSymbolId(int numSymbols)
    {
        // IAID procedure - decode symbol ID using shared contexts
        // T.88 Section 6.4.6 / Annex A.3
        var bits = 0;
        int n = numSymbols - 1;
        while (n > 0) { bits++; n >>= 1; }

        var prev = 1;  // PREV starts at 1 per T.88 A.3
        var id = 0;

        for (var i = 0; i < bits; i++)
        {
            int bit = _decoder.DecodeBit(_textRegionContexts!.IaIdContexts[prev]);
            prev = (prev << 1) | bit;
            id = (id << 1) | bit;
        }

        // T.88 A.3 (3): subtract 2^bits to get final ID
        // Note: This is implicit in our id calculation since we start id at 0
        return id;
    }

    private SymbolDictionary DecodeExportedSymbols(SymbolDictionary newSymbols)
    {
        // T.88 Section 6.5.10 - Export symbol table
        int totalSymbols = _inputSymbols.Count + newSymbols.Count;
        var exportFlags = new bool[totalSymbols];

        int currentExport = false ? 1 : 0; // Start with non-export
        var i = 0;

        while (i < totalSymbols)
        {
            // Decode run length
            int runLength = _decoder.DecodeInt(_iaExContexts);
            if (runLength == int.MinValue)
            {
                // OOB during export flags - this can happen when end-of-data marker
                // was found. If all remaining symbols haven't been processed,
                // and numExported == numNew (exporting all new symbols), treat
                // remaining symbols as exported.

                // If we haven't processed any symbols yet and expected to export all new ones,
                // export all new symbols (common case for simple dictionaries)
                if (i == 0 && _params.NumExportedSymbols == newSymbols.Count)
                {
                    var exported = new SymbolDictionary();
                    for (var k = 0; k < newSymbols.Count; k++)
                        exported.Add(newSymbols[k]);
                    return exported;
                }

                throw new Jbig2DataException("Unexpected OOB in export flags");
            }

            // Handle nonsensical run lengths that would overflow - this can happen
            // when decoder state is corrupted or at end of data. If we get a run
            // length that exceeds remaining symbols and this is the first run
            // (non-export), check if we should just export all symbols.
            if (i == 0 && currentExport == 0 && runLength > totalSymbols &&
                _params.NumExportedSymbols == newSymbols.Count && _inputSymbols.Count == 0)
            {
                var exported = new SymbolDictionary();
                for (var k = 0; k < newSymbols.Count; k++)
                    exported.Add(newSymbols[k]);
                return exported;
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
