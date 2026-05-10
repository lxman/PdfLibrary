namespace Jbig2Decoder.Region
{
    internal enum RefCorner
    {
        BottomLeft = 0,
        TopLeft = 1,
        BottomRight = 2,
        TopRight = 3,
    }

    /// <summary>
    /// Parameters for text region decoding (T.88 §6.4 Table 9).
    /// </summary>
    internal struct TextRegionParams
    {
        public bool SbHuff;
        public bool SbRefine;
        public bool SbDefPixel;          // background pixel (0 = white, 1 = black)
        public int SbCombOp;             // 0=OR, 1=AND, 2=XOR, 3=XNOR, 4=REPLACE
        public bool Transposed;
        public RefCorner RefCorner;
        public int SbDsOffset;
        public uint SbNumInstances;
        public int LogSbStrips;
        public int SbStrips;             // = 1 << LogSbStrips
        public bool SbRTemplate;
        public sbyte[] Sbrat;            // 4 bytes
        public SymbolDictionary[] Dicts;

        /// <summary>
        /// Raw 16-bit Huffman selector field (T.88 §7.4.3.1.1). Only meaningful when
        /// <see cref="SbHuff"/> is true; the bit packing selects the standard tables
        /// (F/G/H/I/J/K/L/M/N/O) used for FS, DS, DT, RDW, RDH, RDX, RDY, and RSIZE.
        /// </summary>
        public ushort SbHuffFlags;

        /// <summary>
        /// User-defined Huffman tables in selector order: Fs, Ds, Dt, Rdw, Rdh,
        /// Rdx, Rdy, Rsize. Each slot is non-null only when its corresponding
        /// selector in <see cref="SbHuffFlags"/> indicates user-defined (T.88
        /// §7.4.3.1.7 fixes the slot order — caller must resolve referred-to
        /// segment 53s into these slots before calling the decoder).
        /// </summary>
        public Huffman.HuffmanParams?[]? UserTables;

        /// <summary>
        /// When non-null, use this pre-built table for symbol-ID decoding
        /// instead of reading the 35-runcode prelude from the bit stream.
        /// Used by symbol-dictionary multi-instance refagg (T.88 §6.5.8.2.4)
        /// where pdfium-style trivial fixed-length codes apply (each symbol
        /// keyed by SBSYMCODELEN raw bits — see PDFium's SddProc).
        /// </summary>
        public Huffman.HuffmanParams? PrebuiltSbSymCodes;

        /// <summary>
        /// Caller-supplied generic-refinement arith stats array. When set, the
        /// text region's per-instance refinement reuses this array instead of
        /// allocating its own; required by SD multi-instance refagg where the
        /// stats persist across all symbol decodes in the dictionary.
        /// </summary>
        public byte[]? SharedGrStats;
    }
}
