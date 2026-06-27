using System;
using System.Collections.Generic;
using FontParser.Tables.Cmap;

namespace FontParser.Subsetting
{
    /// <summary>
    /// Rebuilds the <c>cmap</c> table for a TrueType subset.
    ///
    /// Strategy:
    ///   1. Walk the original cmap to enumerate every (codepoint → oldGID) pair.
    ///      We scan the full BMP range [0x0000..0xFFFF] using the font's own
    ///      <see cref="CmapTable.GetGlyphId"/> so we don't need to duplicate the
    ///      segment-walking logic for every subtable format.
    ///   2. Keep only pairs whose old GID is in <see cref="GlyphIdRemap.OldToNew"/>;
    ///      remap the GID to the new value.
    ///   3. Emit a single-subtable cmap with:
    ///        • cmap header (version=0, numTables=1)
    ///        • one EncodingRecord (platformId=3 / Windows, encodingId=1 / Unicode BMP)
    ///        • one Format-4 subtable over the retained codepoints.
    ///
    /// Format-4 encoding:
    ///   The spec uses segments [startCode..endCode] with an idDelta or a glyphIdArray.
    ///   We use the simplest possible approach: one segment per contiguous run of
    ///   codepoints whose GIDs also form a contiguous ascending run (idDelta can cover
    ///   them all).  For non-contiguous GID runs within a contiguous codepoint range we
    ///   either split segments or use the glyphIdArray mechanism.
    ///
    ///   For small subsets this produces compact output.  The final segment is always
    ///   [0xFFFF..0xFFFF, delta=1, rangeOffset=0] (the required terminator that maps
    ///   nothing — 0xFFFF+1 wraps to 0).
    /// </summary>
    public static class CmapRebuilder
    {
        /// <summary>
        /// Builds the subset <c>cmap</c> table bytes.
        /// </summary>
        /// <param name="originalCmap">Parsed <see cref="CmapTable"/> from the original font.</param>
        /// <param name="remap">GID mapping produced by <see cref="GlyphIdRemap"/>.</param>
        /// <returns>Raw bytes of a valid cmap table with a single Format-4 BMP subtable.</returns>
        public static byte[] Rebuild(CmapTable originalCmap, GlyphIdRemap remap)
        {
            if (originalCmap is null) throw new ArgumentNullException(nameof(originalCmap));
            if (remap is null) throw new ArgumentNullException(nameof(remap));

            // --- Step 1: enumerate retained (codepoint, newGid) pairs ---
            // Scan full BMP. This is at most 65536 iterations — cheap.
            var pairs = new SortedList<ushort, ushort>(); // codepoint → newGid
            for (var cp = 0; cp <= 0xFFFF; cp++)
            {
                ushort oldGid = originalCmap.GetGlyphId((ushort)cp);
                if (oldGid == 0 && cp != 0) continue; // mapping to .notdef means not mapped

                if (remap.OldToNew.TryGetValue(oldGid, out ushort newGid))
                {
                    pairs[(ushort)cp] = newGid;
                }
            }

            // --- Step 2: build Format-4 segments ---
            List<Segment> segments = BuildFormat4Segments(pairs);

            // --- Step 3: serialise ---
            return SerialiseFormat4Cmap(segments);
        }

        // -----------------------------------------------------------------------
        // Segment building
        // -----------------------------------------------------------------------

        private readonly struct Segment
        {
            public readonly ushort StartCode;
            public readonly ushort EndCode;
            public readonly short IdDelta;
            public readonly ushort[] GlyphIds; // non-null & length>0 → use idRangeOffset + glyphIdArray

            public Segment(ushort startCode, ushort endCode, short idDelta)
            {
                StartCode = startCode;
                EndCode = endCode;
                IdDelta = idDelta;
                GlyphIds = Array.Empty<ushort>();
            }

            public Segment(ushort startCode, ushort endCode, ushort[] glyphIds)
            {
                StartCode = startCode;
                EndCode = endCode;
                IdDelta = 0;
                GlyphIds = glyphIds;
            }
        }

        /// <summary>
        /// Convert (codepoint → newGid) pairs into Format-4 segments.
        ///
        /// We walk the sorted pairs and group consecutive codepoints where the GID
        /// difference is constant (i.e., idDelta can cover the whole run without a
        /// glyphIdArray).  When consecutive codepoints do NOT form a linear GID run
        /// we use a glyphIdArray segment instead.
        /// </summary>
        private static List<Segment> BuildFormat4Segments(SortedList<ushort, ushort> pairs)
        {
            var segments = new List<Segment>();
            if (pairs.Count == 0)
            {
                // Terminator only.
                segments.Add(new Segment(0xFFFF, 0xFFFF, 1));
                return segments;
            }

            IList<ushort> keys   = pairs.Keys;
            IList<ushort> values = pairs.Values;

            var i = 0;
            while (i < keys.Count)
            {
                ushort segStart = keys[i];
                ushort segStartGid = values[i];

                // Determine the idDelta if this first codepoint starts a linear run.
                var delta = (short)(segStartGid - segStart);

                // Extend as far as the linear delta holds and codepoints are contiguous.
                int j = i;
                while (j + 1 < keys.Count
                    && keys[j + 1] == keys[j] + 1              // contiguous codepoints
                    && values[j + 1] == values[j] + 1)         // contiguous GIDs (same delta)
                {
                    j++;
                }

                if (j > i)
                {
                    // We have a linear run of at least 2.  Emit a delta segment.
                    segments.Add(new Segment(segStart, keys[j], delta));
                    i = j + 1;
                }
                else
                {
                    // Single codepoint or first of non-linear run.
                    // Look ahead for contiguous codepoints (any GID pattern).
                    int k = i;
                    while (k + 1 < keys.Count && keys[k + 1] == keys[k] + 1)
                        k++;

                    // Emit a glyphIdArray segment for [i..k].
                    int len = k - i + 1;
                    var gids = new ushort[len];
                    for (var m = 0; m < len; m++)
                        gids[m] = values[i + m];
                    segments.Add(new Segment(segStart, keys[k], gids));
                    i = k + 1;
                }
            }

            // Terminator segment required by the spec.
            segments.Add(new Segment(0xFFFF, 0xFFFF, 1));
            return segments;
        }

        // -----------------------------------------------------------------------
        // Serialisation
        // -----------------------------------------------------------------------

        private static byte[] SerialiseFormat4Cmap(List<Segment> segments)
        {
            int segCount = segments.Count;
            int segCountX2 = segCount * 2;

            // searchRange = 2 × (2^floor(log2(segCount)))
            var pow = 1;
            while (pow * 2 <= segCount) pow *= 2;
            int searchRange = pow * 2;
            int entrySelector = Log2(pow);
            int rangeShift = segCountX2 - searchRange;

            // Flatten the glyphIdArray for all glyphIdArray segments.
            // We also need to compute idRangeOffset for each segment.
            var glyphIdArrayParts = new List<ushort[]>();
            var idRangeOffsets = new ushort[segCount];
            for (var si = 0; si < segCount; si++)
            {
                Segment seg = segments[si];
                if (seg.GlyphIds is { Length: > 0 })
                {
                    // idRangeOffset is relative to the idRangeOffset array entry.
                    // It points into the glyphIdArray which follows the four arrays.
                    // The arrays (endCode, pad, startCode, idDelta, idRangeOffset) are
                    // each segCount entries × 2 bytes.
                    // Within the idRangeOffset array, this entry is at index si, so the
                    // relative byte offset to the glyphIdArray start is:
                    //   (segCount - si) * 2  [remainder of idRangeOffset array]
                    //   + (sum of all previous glyphIdArray entries) * 2
                    var glyphIdArrayOffset = 0;
                    for (var prev = 0; prev < glyphIdArrayParts.Count; prev++)
                        glyphIdArrayOffset += glyphIdArrayParts[prev].Length;
                    int remainingRangeOffsetEntries = segCount - si;
                    idRangeOffsets[si] = (ushort)((remainingRangeOffsetEntries + glyphIdArrayOffset) * 2);
                    glyphIdArrayParts.Add(seg.GlyphIds);
                }
                else
                {
                    idRangeOffsets[si] = 0;
                }
            }

            // Flatten glyphIdArray.
            var totalGlyphIds = 0;
            foreach (ushort[]? part in glyphIdArrayParts) totalGlyphIds += part.Length;
            var glyphIdArray = new ushort[totalGlyphIds];
            var gIdx = 0;
            foreach (ushort[]? part in glyphIdArrayParts)
            {
                foreach (ushort g in part) glyphIdArray[gIdx++] = g;
            }

            // Format-4 fixed header: format(2) + length(2) + language(2) +
            //   segCountX2(2) + searchRange(2) + entrySelector(2) + rangeShift(2) = 14 bytes.
            // Arrays: endCode(segCount×2) + pad(2) + startCode(segCount×2) +
            //         idDelta(segCount×2) + idRangeOffset(segCount×2) +
            //         glyphIdArray(totalGlyphIds×2).
            int subtableLength = 14
                + segCount * 2   // endCode
                + 2              // reservedPad
                + segCount * 2   // startCode
                + segCount * 2   // idDelta
                + segCount * 2   // idRangeOffset
                + totalGlyphIds * 2;

            // cmap outer table: version(2) + numTables(2) + EncodingRecord(8) = 12 bytes.
            var subtableOffset = 12;
            int totalLength = subtableOffset + subtableLength;

            var buf = new byte[totalLength];
            var p = 0;

            // --- cmap header ---
            WriteU16(buf, ref p, 0);          // version
            WriteU16(buf, ref p, 1);          // numTables

            // --- EncodingRecord: platform 3 (Windows), encoding 1 (Unicode BMP) ---
            WriteU16(buf, ref p, 3);          // platformId
            WriteU16(buf, ref p, 1);          // encodingId
            WriteU32(buf, ref p, (uint)subtableOffset);  // offset to subtable

            // --- Format-4 header ---
            WriteU16(buf, ref p, 4);          // format
            WriteU16(buf, ref p, (ushort)subtableLength);
            WriteU16(buf, ref p, 0);          // language = 0

            WriteU16(buf, ref p, (ushort)segCountX2);
            WriteU16(buf, ref p, (ushort)searchRange);
            WriteU16(buf, ref p, (ushort)entrySelector);
            WriteU16(buf, ref p, (ushort)rangeShift);

            // endCode array
            for (var si = 0; si < segCount; si++)
                WriteU16(buf, ref p, segments[si].EndCode);

            WriteU16(buf, ref p, 0);          // reservedPad

            // startCode array
            for (var si = 0; si < segCount; si++)
                WriteU16(buf, ref p, segments[si].StartCode);

            // idDelta array
            for (var si = 0; si < segCount; si++)
            {
                Segment seg = segments[si];
                short delta = seg.GlyphIds is { Length: > 0 } ? (short)0 : seg.IdDelta;
                WriteU16(buf, ref p, (ushort)delta);
            }

            // idRangeOffset array
            for (var si = 0; si < segCount; si++)
                WriteU16(buf, ref p, idRangeOffsets[si]);

            // glyphIdArray
            for (var gi = 0; gi < totalGlyphIds; gi++)
                WriteU16(buf, ref p, glyphIdArray[gi]);

            return buf;
        }

        // -----------------------------------------------------------------------
        // Inline helpers
        // -----------------------------------------------------------------------

        private static void WriteU16(byte[] buf, ref int pos, ushort value)
        {
            buf[pos++] = (byte)(value >> 8);
            buf[pos++] = (byte)(value & 0xFF);
        }

        private static void WriteU32(byte[] buf, ref int pos, uint value)
        {
            buf[pos++] = (byte)(value >> 24);
            buf[pos++] = (byte)(value >> 16);
            buf[pos++] = (byte)(value >> 8);
            buf[pos++] = (byte)(value & 0xFF);
        }

        private static int Log2(int n)
        {
            var r = 0;
            while (n > 1) { n >>= 1; r++; }
            return r;
        }
    }
}
