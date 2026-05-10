namespace Jbig2Decoder.Region
{
    /// <summary>
    /// Parameters for generic region decoding (T.88 §6.2 / §7.4.6 Table 34).
    /// </summary>
    internal struct GenericRegionParams
    {
        public bool Mmr;
        public int GbTemplate;     // 0..3
        public bool TpgdOn;
        public bool UseSkip;
        public Image.Bitmap? Skip;  // when UseSkip == true: per-pixel skip mask
                                    // (T.88 §6.2.5.3) — pixels where Skip=1 are
                                    // not in the bitstream; decoder writes 0.
        public sbyte[] Gbat;       // 8 bytes: (gbat0_x, gbat0_y, gbat1_x, gbat1_y, gbat2_x, gbat2_y, gbat3_x, gbat3_y)

        public static GenericRegionParams DefaultTemplate0()
        {
            return new GenericRegionParams
            {
                GbTemplate = 0,
                Gbat = new sbyte[] { 3, -1, -3, -1, 2, -2, -2, -2 },
            };
        }
    }
}
