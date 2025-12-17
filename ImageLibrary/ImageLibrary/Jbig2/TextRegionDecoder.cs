using System;

namespace ImageLibrary.Jbig2;

/// <summary>
/// Decodes text region segments.
/// T.88 Section 6.4 and 7.4.3.
/// </summary>
internal sealed class TextRegionDecoder
{
    private readonly ArithmeticDecoder _decoder;
    private readonly TextRegionParams _params;
    private readonly SymbolDictionary[] _symbolDictionaries;
    private readonly int _totalSymbols;
    private readonly int _symbolCountForIaid;  // May differ from _totalSymbols during multi-symbol aggregation
    private readonly Jbig2DecoderOptions _options;

    // Arithmetic coding contexts
    private readonly ArithmeticDecoder.Context[] _iaDtContexts;  // Strip delta T
    private readonly ArithmeticDecoder.Context[] _iaFsContexts;  // First S
    private readonly ArithmeticDecoder.Context[] _iaDsContexts;  // Delta S
    private readonly ArithmeticDecoder.Context[] _iaItContexts;  // Instance T
    private readonly ArithmeticDecoder.Context[] _iaIdContexts;  // Symbol ID
    private readonly ArithmeticDecoder.Context _iaRiContext;  // Refinement indicator (single bit)
    private readonly ArithmeticDecoder.Context[] _iaRdwContexts; // Refinement delta width
    private readonly ArithmeticDecoder.Context[] _iaRdhContexts; // Refinement delta height
    private readonly ArithmeticDecoder.Context[] _iaRdxContexts; // Refinement dx
    private readonly ArithmeticDecoder.Context[] _iaRdyContexts; // Refinement dy
    private readonly ArithmeticDecoder.Context[]? _grContexts;   // Shared GR contexts for refinement

    public TextRegionDecoder(
        ArithmeticDecoder decoder,
        TextRegionParams parameters,
        SymbolDictionary[] symbolDictionaries,
        Jbig2DecoderOptions? options = null,
        TextRegionSharedContexts? sharedContexts = null)
    {
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _params = parameters ?? throw new ArgumentNullException(nameof(parameters));
        _symbolDictionaries = symbolDictionaries ?? throw new ArgumentNullException(nameof(symbolDictionaries));
        _options = options ?? Jbig2DecoderOptions.Default;

        if (_params.UseHuffman)
            throw new Jbig2UnsupportedException("Huffman-coded text regions not yet implemented");

        // Count total symbols across all dictionaries
        _totalSymbols = 0;
        foreach (var dict in _symbolDictionaries)
            _totalSymbols += dict?.Count ?? 0;

        if (_totalSymbols == 0)
            throw new Jbig2DataException("No symbols available for text region decoding");

        // Use override for IAID context size if specified
        // This is needed for multi-symbol aggregation where the symbol count at decode time
        // is less than the total symbols that will eventually be available
        _symbolCountForIaid = _params.TotalSymbolCountOverride > 0
            ? _params.TotalSymbolCountOverride
            : _totalSymbols;

        // Use shared contexts if provided (for multi-symbol aggregation in symbol dictionary),
        // otherwise create fresh ones
        if (sharedContexts != null)
        {
            _iaDtContexts = sharedContexts.IaDtContexts;
            _iaFsContexts = sharedContexts.IaFsContexts;
            _iaDsContexts = sharedContexts.IaDsContexts;
            _iaItContexts = sharedContexts.IaItContexts;
            _iaRiContext = sharedContexts.IaRiContext;
            _iaRdwContexts = sharedContexts.IaRdwContexts;
            _iaRdhContexts = sharedContexts.IaRdhContexts;
            _iaRdxContexts = sharedContexts.IaRdxContexts;
            _iaRdyContexts = sharedContexts.IaRdyContexts;
            _iaIdContexts = sharedContexts.IaIdContexts;
            _grContexts = sharedContexts.GrContexts;  // Use shared GR contexts from symbol dictionary
        }
        else
        {
            // Create arithmetic coding contexts
            _iaDtContexts = ArithmeticDecoder.CreateIntContexts();
            _iaFsContexts = ArithmeticDecoder.CreateIntContexts();
            _iaDsContexts = ArithmeticDecoder.CreateIntContexts();
            _iaItContexts = ArithmeticDecoder.CreateIntContexts();
            _iaRiContext = new ArithmeticDecoder.Context();  // Single bit context
            _iaRdwContexts = ArithmeticDecoder.CreateIntContexts();
            _iaRdhContexts = ArithmeticDecoder.CreateIntContexts();
            _iaRdxContexts = ArithmeticDecoder.CreateIntContexts();
            _iaRdyContexts = ArithmeticDecoder.CreateIntContexts();

            // Symbol ID contexts - use _symbolCountForIaid for context size
            int idContextSize = GetIdContextSize(_symbolCountForIaid);
            _iaIdContexts = new ArithmeticDecoder.Context[idContextSize];
            for (var i = 0; i < idContextSize; i++)
                _iaIdContexts[i] = new ArithmeticDecoder.Context();
        }
    }

    private static int GetIdContextSize(int numSymbols)
    {
        if (numSymbols <= 1) return 512;
        var bits = 0;
        int n = numSymbols - 1;
        while (n > 0) { bits++; n >>= 1; }
        return Math.Max(512, 1 << bits);
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
        // Note: jbig2dec decodes IADT before the main loop and applies -SBSTRIPS multiplier
        int initialStripT = _decoder.DecodeInt(_iaDtContexts);
        if (initialStripT == int.MinValue)
            throw new Jbig2DataException("Unexpected OOB in initial strip T");

        // Apply the strip size multiplier with sign from SBDSOFFSET
        int stripT = initialStripT * (-_params.StripSize);

        var firstS = 0;
        var instancesDecoded = 0;

        var loopIterations = 0;

        while (instancesDecoded < _params.NumInstances)
        {
            if (++loopIterations > _options.MaxLoopIterations)
                throw new Jbig2ResourceException($"Text region decode iteration limit exceeded ({_options.MaxLoopIterations})");

            // 6.4.6 - Decode strip delta T (DT)
            int dt = _decoder.DecodeInt(_iaDtContexts);
            if (dt == int.MinValue)
                throw new Jbig2DataException("Unexpected OOB in strip delta T");

            stripT += dt * _params.StripSize;

            // 6.4.7 - Decode first S coordinate
            int fs = _decoder.DecodeInt(_iaFsContexts);
            if (fs == int.MinValue)
                throw new Jbig2DataException("Unexpected OOB in first S");

            firstS += fs;
            int curS = firstS;

            // Decode symbols in this strip
            var firstSymbolInStrip = true;
            var innerIterations = 0;
            while (true)
            {
                if (++innerIterations > _options.MaxLoopIterations)
                    throw new Jbig2ResourceException($"Text region strip iteration limit exceeded ({_options.MaxLoopIterations})");

                // For second and subsequent symbols in strip, decode IADS first
                // (matching jbig2dec loop structure where IADS is decoded before instance processing)
                if (!firstSymbolInStrip)
                {
                    // 6.4.12 - Decode delta S for this symbol
                    int ds = _decoder.DecodeInt(_iaDsContexts);
                    if (ds == int.MinValue) // OOB - end of strip
                    {
                        break;
                    }

                    // Advance S by delta
                    curS += ds;
                }
                firstSymbolInStrip = false;

                // 6.4.8 - Instance T within strip
                int instanceT;
                if (_params.StripSize == 1)
                {
                    instanceT = 0;
                }
                else
                {
                    instanceT = _decoder.DecodeInt(_iaItContexts);
                    if (instanceT == int.MinValue)
                        instanceT = 0;
                }

                int t = stripT + instanceT;

                // 6.4.9 - Decode symbol ID
                int symbolId = DecodeSymbolId();

                // Get symbol bitmap
                Bitmap? symbolBitmap = GetSymbol(symbolId);
                if (symbolBitmap == null)
                    throw new Jbig2DataException($"Invalid symbol ID: {symbolId}");

                // 6.4.10 - Check for refinement
                if (_params.UseRefinement)
                {
                    int ri = _decoder.DecodeBit(_iaRiContext);  // RI is a single bit
                    if (ri != 0)
                    {
                        // Apply refinement to this instance
                        symbolBitmap = DecodeRefinedInstance(symbolBitmap);
                    }
                }

                // 6.4.11 - Place symbol on bitmap
                PlaceSymbol(bitmap, symbolBitmap, curS, t);

                // Advance curS by symbol width for next iteration's ds calculation
                curS += _params.Transposed ? symbolBitmap.Height : symbolBitmap.Width;

                instancesDecoded++;

                // Check if we've decoded all expected instances
                if (instancesDecoded >= _params.NumInstances)
                {
                    // Try to decode one more IADS which should return OOB (end of strip)
                    // This consumes the OOB bits which may be important for subsequent decoding
                    int finalDs = _decoder.DecodeInt(_iaDsContexts);
                    if (finalDs != int.MinValue)
                    {
                        // Not OOB - this shouldn't happen
                    }
                    break;
                }
            }
        }

        return bitmap;
    }

    private int DecodeSymbolId()
    {
        // IAID procedure - use _symbolCountForIaid for bit count
        var bits = 0;
        int n = _symbolCountForIaid - 1;
        while (n > 0) { bits++; n >>= 1; }

        var id = 0;
        var contextIndex = 1;

        for (var i = 0; i < bits; i++)
        {
            ArithmeticDecoder.Context ctx = _iaIdContexts[contextIndex];
            int bit = _decoder.DecodeBit(ctx);
            contextIndex = (contextIndex << 1) | bit;
            id = (id << 1) | bit;
        }

        return id;
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
        // T.88 Section 6.4.11.2 - Text region refined symbol instance
        // The refined bitmap has the SAME dimensions as the reference symbol.
        // No RDW, RDH, RDX, RDY are decoded for text region refinement -
        // those are only for symbol dictionary refinement aggregation (6.5.8.2.4).
        // The reference is centered at (0,0).

        int width = reference.Width;
        int height = reference.Height;

        // Use shared GR contexts if available (from symbol dictionary multi-agg),
        // per T.88 6.5.5 step 11 which requires GR contexts to be shared across
        // all refinement operations in a symbol dictionary.
        var refinementDecoder = new RefinementRegionDecoder(
            _decoder,
            reference,
            0,  // refOffsetX = 0 for text region refinement
            0,  // refOffsetY = 0 for text region refinement
            _params.RefinementTemplate,
            _params.RefinementAdaptivePixels,
            _options,
            _grContexts);  // Pass shared GR contexts if available

        return refinementDecoder.Decode(width, height);
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

/// <summary>
/// Parameters for text region decoding.
/// T.88 Section 7.4.3.1.
/// </summary>
internal sealed class TextRegionParams
{
    /// <summary>
    /// Use Huffman coding (true) or arithmetic coding (false).
    /// </summary>
    public bool UseHuffman { get; set; }

    /// <summary>
    /// Use symbol refinement.
    /// </summary>
    public bool UseRefinement { get; set; }

    /// <summary>
    /// Strip size (2^LOGSBSTRIPS).
    /// </summary>
    public int StripSize { get; set; } = 1;

    /// <summary>
    /// Reference corner for symbol placement.
    /// </summary>
    public ReferenceCorner ReferenceCorner { get; set; } = ReferenceCorner.TopLeft;

    /// <summary>
    /// Transpose coordinates (swap S and T).
    /// </summary>
    public bool Transposed { get; set; }

    /// <summary>
    /// Combination operator for placing symbols.
    /// </summary>
    public CombinationOperator CombinationOp { get; set; } = CombinationOperator.Or;

    /// <summary>
    /// Default pixel value for the region.
    /// </summary>
    public int DefaultPixel { get; set; }

    /// <summary>
    /// Strip delta S offset.
    /// </summary>
    public int StripDeltaOffset { get; set; }

    /// <summary>
    /// Refinement template (0 or 1).
    /// </summary>
    public int RefinementTemplate { get; set; }

    /// <summary>
    /// Number of symbol instances to decode.
    /// </summary>
    public int NumInstances { get; set; }

    /// <summary>
    /// Adaptive template pixels for refinement.
    /// </summary>
    public (int dx, int dy)[] RefinementAdaptivePixels { get; set; } = [];

    /// <summary>
    /// Log of strip size (LOGSBSTRIPS).
    /// </summary>
    public int LogStripSize { get; set; }

    /// <summary>
    /// Override for total symbol count (SBNUMSYMS) used for IAID context size.
    /// When 0 (default), uses the actual count from symbol dictionaries.
    /// Set this when the symbol dictionaries don't contain all the symbols
    /// that were planned (e.g., during multi-symbol aggregation in symbol dictionary decoding).
    /// </summary>
    public int TotalSymbolCountOverride { get; set; }

    // Huffman table selections (7.4.3.1.6)
    // Values 0-2 select standard tables, 3 = custom table

    /// <summary>Huffman table selection for FS (first S). 0=B.6, 1=B.7, 3=custom</summary>
    public int HuffmanFS { get; set; }

    /// <summary>Huffman table selection for DS (delta S). 0=B.8, 1=B.9, 2=B.10, 3=custom</summary>
    public int HuffmanDS { get; set; }

    /// <summary>Huffman table selection for DT (delta T). 0=B.11, 1=B.12, 2=B.13, 3=custom</summary>
    public int HuffmanDT { get; set; }

    /// <summary>Huffman table selection for RDW. 0=B.14, 1=B.15, 3=custom</summary>
    public int HuffmanRDW { get; set; }

    /// <summary>Huffman table selection for RDH. 0=B.14, 1=B.15, 3=custom</summary>
    public int HuffmanRDH { get; set; }

    /// <summary>Huffman table selection for RDX. 0=B.14, 1=B.15, 3=custom</summary>
    public int HuffmanRDX { get; set; }

    /// <summary>Huffman table selection for RDY. 0=B.14, 1=B.15, 3=custom</summary>
    public int HuffmanRDY { get; set; }

    /// <summary>Huffman table selection for RSIZE. 0=B.1, 3=custom</summary>
    public int HuffmanRSIZE { get; set; }
}

/// <summary>
/// Reference corner for symbol placement.
/// T.88 Table 17 - the 2-bit value from segment flags maps as follows.
/// </summary>
internal enum ReferenceCorner
{
    BottomLeft = 0,
    TopLeft = 1,
    BottomRight = 2,
    TopRight = 3
}

/// <summary>
/// Shared arithmetic contexts for text region decoding.
/// Used when multiple text regions within a symbol dictionary need to share state.
/// </summary>
internal sealed class TextRegionSharedContexts
{
    public ArithmeticDecoder.Context[] IaDtContexts { get; }
    public ArithmeticDecoder.Context[] IaFsContexts { get; }
    public ArithmeticDecoder.Context[] IaDsContexts { get; }
    public ArithmeticDecoder.Context[] IaItContexts { get; }
    public ArithmeticDecoder.Context IaRiContext { get; }  // Single bit context
    public ArithmeticDecoder.Context[] IaRdwContexts { get; }
    public ArithmeticDecoder.Context[] IaRdhContexts { get; }
    public ArithmeticDecoder.Context[] IaRdxContexts { get; }
    public ArithmeticDecoder.Context[] IaRdyContexts { get; }
    public ArithmeticDecoder.Context[] IaIdContexts { get; }

    /// <summary>
    /// Generic refinement (GR) contexts shared from symbol dictionary.
    /// Per T.88 6.5.5 step 11, GR contexts should be shared across all refinement
    /// operations in a symbol dictionary, including those in multi-symbol aggregation.
    /// </summary>
    public ArithmeticDecoder.Context[]? GrContexts { get; set; }

    public TextRegionSharedContexts(int totalSymbols)
    {
        IaDtContexts = ArithmeticDecoder.CreateIntContexts();
        IaFsContexts = ArithmeticDecoder.CreateIntContexts();
        IaDsContexts = ArithmeticDecoder.CreateIntContexts();
        IaItContexts = ArithmeticDecoder.CreateIntContexts();
        IaRiContext = new ArithmeticDecoder.Context();  // Single bit
        IaRdwContexts = ArithmeticDecoder.CreateIntContexts();
        IaRdhContexts = ArithmeticDecoder.CreateIntContexts();
        IaRdxContexts = ArithmeticDecoder.CreateIntContexts();
        IaRdyContexts = ArithmeticDecoder.CreateIntContexts();

        // Symbol ID contexts - size based on total symbols
        int idContextSize = GetIdContextSize(totalSymbols);
        IaIdContexts = new ArithmeticDecoder.Context[idContextSize];
        for (var i = 0; i < idContextSize; i++)
            IaIdContexts[i] = new ArithmeticDecoder.Context();
    }

    private static int GetIdContextSize(int numSymbols)
    {
        if (numSymbols <= 1) return 512;
        var bits = 0;
        int n = numSymbols - 1;
        while (n > 0) { bits++; n >>= 1; }
        return Math.Max(512, 1 << bits);
    }
}
