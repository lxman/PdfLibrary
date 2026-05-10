namespace Jbig2Decoder.Region
{
    /// <summary>
    /// Parameters parsed from the pattern dictionary segment header (T.88 §7.4.4.1).
    /// </summary>
    internal struct PatternDictionaryParams
    {
        public bool HdMmr;          // bit 0 of flags
        public int HdTemplate;      // bits 1-2 of flags (only used when HdMmr = 0)
        public int HdPw;            // pattern width  (1 byte)
        public int HdPh;            // pattern height (1 byte)
        public uint GrayMax;        // 4 bytes BE: number of patterns - 1
    }
}
