namespace Jbig2Decoder.Region
{
    /// <summary>
    /// Parameters parsed from a halftone region segment header (T.88 §7.4.5.1).
    /// </summary>
    internal struct HalftoneRegionParams
    {
        public bool HMmr;            // bit 0 of flags
        public int  HTemplate;       // bits 1-2 of flags (only used when HMmr = 0)
        public bool HEnableSkip;     // bit 3 of flags
        public int  HCombOp;         // bits 4-6 of flags (T.88 §7.4 Table 5)
        public int  HDefPixel;       // bit 7 of flags

        public uint Hgw;             // grid width  (cells)
        public uint Hgh;             // grid height (cells)
        public int  Hgx;             // grid origin X, 8.8 fixed point (signed)
        public int  Hgy;             // grid origin Y, 8.8 fixed point (signed)
        public int  Hrx;             // grid vector X-component, 8.8 signed
        public int  Hry;             // grid vector Y-component, 8.8 signed

        public PatternDictionary Patterns;
    }
}
