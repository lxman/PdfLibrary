using Jbig2Decoder.Image;

namespace Jbig2Decoder.Region
{
    /// <summary>
    /// Result of decoding a symbol dictionary segment — the array of exported
    /// symbol bitmaps that text regions reference by index.
    /// </summary>
    internal sealed class SymbolDictionary
    {
        public Bitmap[] Glyphs { get; }
        public int Count => Glyphs.Length;

        /// <summary>
        /// When the SD segment had its "retain bitmap coding context" flag set
        /// (T.88 §7.4.2.1.1 bit 9, 0x200), the final state of the generic-region
        /// stats array is preserved here so a later SD with bit 8 set
        /// (SDREFAGG_REUSE / SDRETAINBMC) can adopt it as its starting state
        /// instead of reinitialising. Both bytes-arrays are null for SDs that
        /// don't request retention.
        /// </summary>
        public byte[]? RetainedGbStats;
        public byte[]? RetainedGrStats;

        public SymbolDictionary(Bitmap[] glyphs)
        {
            Glyphs = glyphs;
        }
    }
}
