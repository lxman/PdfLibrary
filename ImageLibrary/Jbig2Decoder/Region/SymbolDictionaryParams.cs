using Jbig2Decoder.Huffman;

namespace Jbig2Decoder.Region
{
    /// <summary>
    /// Parameters for symbol dictionary decoding (T.88 Table 13).
    /// </summary>
    internal struct SymbolDictionaryParams
    {
        public bool SdHuff;
        public bool SdRefAgg;
        public int SdTemplate;
        public int SdRTemplate;
        public sbyte[] Sdat;        // 8 bytes
        public sbyte[] Sdrat;       // 4 bytes
        public uint SdNumInSyms;
        public uint SdNumNewSyms;
        public uint SdNumExSyms;
        public SymbolDictionary? SdInSyms;

        /// <summary>
        /// Raw 16-bit segment header flags word (T.88 §7.4.2.1.1). Only the
        /// Huffman selector bits 2-7 are meaningful here, and only when
        /// <see cref="SdHuff"/> is true. SDHUFFDH (bits 2-3): 0=B.4, 1=B.5;
        /// SDHUFFDW (bits 4-5): 0=B.2, 1=B.3; SDHUFFBMSIZE (bit 6): 0=B.1;
        /// SDHUFFAGGINST (bit 7): 0=B.1.
        /// </summary>
        public ushort SdHuffFlags;

        /// <summary>
        /// User-defined Huffman tables in selector order: 0=Dh, 1=Dw,
        /// 2=BmSize, 3=AggInst (T.88 §7.4.2.1.6). Each slot is non-null only
        /// when its selector indicates user-defined; caller resolves the
        /// referred-to segment 53s into these slots.
        /// </summary>
        public HuffmanParams?[]? UserTables;

        /// <summary>
        /// T.88 §7.4.2.1.1 bit 8 (SDREFAGG_REUSE / SDRETAINBMC = "use bitmap
        /// coding context"): when true, the decoder seeds the generic-region
        /// arith stats with <see cref="SeedGbStats"/> and refinement stats
        /// with <see cref="SeedGrStats"/> instead of starting from zero.
        /// </summary>
        public bool UseRetainedContext;
        public byte[]? SeedGbStats;
        public byte[]? SeedGrStats;

        /// <summary>
        /// T.88 §7.4.2.1.1 bit 9 (SDRETAIN = "retain bitmap coding context"):
        /// when true, the decoder writes the final generic-region/refinement
        /// arith stats arrays into the resulting <see cref="SymbolDictionary"/>
        /// for a later SD to seed from.
        /// </summary>
        public bool RetainContext;
    }
}
