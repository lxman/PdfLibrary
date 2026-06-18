using System;
using FontParser.Tables.Hmtx;

namespace FontParser.Subsetting
{
    /// <summary>
    /// Rebuilds the <c>hmtx</c> table for a TrueType subset and computes the
    /// correct <c>hhea.numberOfHMetrics</c> value.
    ///
    /// The OpenType spec allows fonts to store the last N identical advance widths
    /// as a single trailing entry in hmtx (the "compressed" form where glyphs beyond
    /// numberOfHMetrics all share the width of the last full record).  For a small
    /// subset we always emit full records for every retained glyph — this is
    /// maximally compatible and trivially correct.  numberOfHMetrics therefore equals
    /// the retained glyph count.
    ///
    /// Wire format (big-endian per table record):
    ///   For each of the numberOfHMetrics glyphs:
    ///     AdvanceWidth  uint16
    ///     Lsb           int16
    ///   For any remaining glyphs (numGlyphs − numberOfHMetrics):
    ///     Lsb only      int16
    /// Since we emit numberOfHMetrics == numGlyphs there are no trailing LSB-only
    /// entries in the output.
    /// </summary>
    public static class HmtxRebuilder
    {
        /// <summary>
        /// Builds the subset <c>hmtx</c> bytes and the required <c>numberOfHMetrics</c>.
        /// </summary>
        /// <param name="originalHmtx">
        /// Parsed <see cref="HmtxTable"/> from the original font (already processed).
        /// </param>
        /// <param name="remap">GID mapping produced by <see cref="GlyphIdRemap"/>.</param>
        /// <returns>
        /// A tuple of (hmtxBytes, numberOfHMetrics) ready to write into the subset sfnt.
        /// </returns>
        public static (byte[] HmtxBytes, ushort NumberOfHMetrics) Rebuild(
            HmtxTable originalHmtx,
            GlyphIdRemap remap)
        {
            if (originalHmtx is null) throw new ArgumentNullException(nameof(originalHmtx));
            if (remap is null) throw new ArgumentNullException(nameof(remap));

            int n = remap.Count;

            // 4 bytes per glyph: AdvanceWidth (uint16) + Lsb (int16).
            var bytes = new byte[n * 4];
            int pos = 0;

            for (int newGid = 0; newGid < n; newGid++)
            {
                ushort oldGid = remap.NewToOld[newGid];
                ushort aw = originalHmtx.GetAdvanceWidth(oldGid);
                short lsb = originalHmtx.GetLeftSideBearing(oldGid);

                // Big-endian AdvanceWidth
                bytes[pos++] = (byte)(aw >> 8);
                bytes[pos++] = (byte)(aw & 0xFF);
                // Big-endian Lsb (signed, but same wire encoding)
                bytes[pos++] = (byte)((ushort)lsb >> 8);
                bytes[pos++] = (byte)((ushort)lsb & 0xFF);
            }

            return (bytes, (ushort)n);
        }
    }
}
