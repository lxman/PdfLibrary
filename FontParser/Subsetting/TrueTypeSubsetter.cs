using System;
using System.Collections.Generic;
using System.Text;
using FontParser.Tables.Cmap;

namespace FontParser.Subsetting
{
    /// <summary>
    /// Produces a valid subset TrueType sfnt from an <see cref="SfntFont"/> and a set of
    /// glyph IDs.  The returned bytes can be parsed by <see cref="SfntFont"/> and fed
    /// directly to a PDF font stream.
    ///
    /// Pipeline:
    ///   1. <see cref="GlyphClosure.Compute"/> — transitive GID closure.
    ///   2. <see cref="GlyphIdRemap"/> — contiguous new-GID assignment.
    ///   3. <see cref="GlyfLocaRebuilder.Rebuild"/> — rebuilt glyf + loca.
    ///   4. <see cref="HmtxRebuilder.Rebuild"/> — rebuilt hmtx.
    ///   5. <see cref="CmapRebuilder.Rebuild"/> — rebuilt cmap (Format-4 BMP).
    ///   6. Patch head (indexToLocFormat, checkSumAdjustment) and maxp (numGlyphs),
    ///      hhea (numberOfHMetrics).
    ///   7. Pass through optional tables unchanged: name, post, cvt , fpgm, prep, gasp, OS/2.
    ///   8. Re-serialise as a valid sfnt (offset table + table directory + data).
    ///   9. Compute per-table checksums and the whole-font checkSumAdjustment in head.
    /// </summary>
    public static class TrueTypeSubsetter
    {
        /// <summary>
        /// Create a subset font, returning the old→new GID mapping so callers can rewrite
        /// /CIDToGIDMap entries.
        /// </summary>
        /// <param name="font">Fully parsed source TrueType font.</param>
        /// <param name="requestedGlyphIds">GIDs that must be retained (closure is computed internally).</param>
        /// <param name="oldToNewGid">On return: old GID → new (compacted) GID for all retained glyphs.</param>
        /// <returns>Raw bytes of a self-consistent subset sfnt font.</returns>
        public static byte[] Subset(SfntFont font, IEnumerable<ushort> requestedGlyphIds,
            out IReadOnlyDictionary<ushort, ushort> oldToNewGid)
        {
            if (font is null) throw new ArgumentNullException(nameof(font));
            if (requestedGlyphIds is null) throw new ArgumentNullException(nameof(requestedGlyphIds));

            // 1. Closure
            var closure = GlyphClosure.Compute(font, requestedGlyphIds);
            // 2. Remap
            var remap = new GlyphIdRemap(closure);
            oldToNewGid = remap.OldToNew;

            // Delegate the rest to the existing implementation, but we've already computed
            // closure+remap so we replicate the remainder inline to avoid double-computing.
            return SubsetWithRemap(font, remap);
        }

        /// <summary>
        /// Create a subset font.
        /// </summary>
        /// <param name="font">Fully parsed source TrueType font.</param>
        /// <param name="requestedGlyphIds">
        /// The glyph IDs that must be retained.  GID 0 (.notdef) is always added
        /// unless the closure is empty.  Components of composite glyphs are added
        /// automatically via <see cref="GlyphClosure"/>.
        /// </param>
        /// <returns>Raw bytes of a self-consistent subset sfnt font.</returns>
        public static byte[] Subset(SfntFont font, IEnumerable<ushort> requestedGlyphIds)
        {
            if (font is null) throw new ArgumentNullException(nameof(font));
            if (requestedGlyphIds is null) throw new ArgumentNullException(nameof(requestedGlyphIds));

            var closure = GlyphClosure.Compute(font, requestedGlyphIds);
            var remap = new GlyphIdRemap(closure);
            return SubsetWithRemap(font, remap);
        }

        // Shared implementation used by both Subset overloads.
        private static byte[] SubsetWithRemap(SfntFont font, GlyphIdRemap remap)
        {
            // Closure and remap already computed by caller.

            // 3. glyf + loca
            byte[] origGlyf = font.GetTableBytes("glyf")
                ?? throw new InvalidOperationException("Source font has no glyf table — not a TrueType font.");
            var origLoca = font.Loca
                ?? throw new InvalidOperationException("Source font has no loca table.");

            GlyfLocaRebuilder.RebuildResult glyfLoca =
                GlyfLocaRebuilder.Rebuild(origGlyf, origLoca, remap, font.Glyf);

            // 4. hmtx
            var origHmtx = font.Hmtx
                ?? throw new InvalidOperationException("Source font has no hmtx table.");
            (byte[] hmtxBytes, ushort numberOfHMetrics) =
                HmtxRebuilder.Rebuild(origHmtx, remap);

            // 5. cmap
            var origCmap = font.Cmap
                ?? throw new InvalidOperationException("Source font has no cmap table.");
            byte[] cmapBytes = CmapRebuilder.Rebuild(origCmap, remap);

            // 6a. Patch maxp — copy original bytes and overwrite numGlyphs at offset 4.
            byte[] maxpBytes = CloneTable(font, "maxp")
                ?? throw new InvalidOperationException("Source font has no maxp table.");
            // maxp layout: version(4 bytes) + numGlyphs(2 bytes) at offset 4.
            WriteBEU16(maxpBytes, 4, (ushort)remap.Count);

            // 6b. Patch hhea — copy original and set numberOfHMetrics (last field, offset 34).
            byte[] hheaBytes = CloneTable(font, "hhea")
                ?? throw new InvalidOperationException("Source font has no hhea table.");
            // hhea is 36 bytes; numberOfHMetrics is at offset 34.
            WriteBEU16(hheaBytes, 34, numberOfHMetrics);

            // 6c. Patch head — copy original, set indexToLocFormat at offset 50.
            //     checkSumAdjustment (offset 8) will be fixed after full serialisation.
            byte[] headBytes = CloneTable(font, "head")
                ?? throw new InvalidOperationException("Source font has no head table.");
            // head layout (offset 50 = indexToLocFormat):
            //   0: majorVersion(2) + minorVersion(2) + fontRevision(4) +
            //      checkSumAdjustment(4) + magicNumber(4) + flags(2) + unitsPerEm(2) = 24
            //  24: created(8) + modified(8) = 40
            //  40: xMin(2)+yMin(2)+xMax(2)+yMax(2) + macStyle(2)+lowestRecPpem(2) = 52
            //  52: fontDirectionHint(2) + indexToLocFormat(2) + glyphDataFormat(2)
            // So indexToLocFormat is at byte offset 50.
            WriteBEU16(headBytes, 50, (ushort)(glyfLoca.UseShortLoca ? 0 : 1));
            // Zero out checkSumAdjustment before computing whole-font checksum.
            WriteBEU32(headBytes, 8, 0u);

            // 7. Collect tables. Required order per spec (helps some renderers):
            //    head, hhea, maxp, OS/2, hmtx, cmap, loca, glyf, then optional.
            var tables = new List<(string tag, byte[] data)>();

            AddTable(tables, "head", headBytes);
            AddTable(tables, "hhea", hheaBytes);
            AddTable(tables, "maxp", maxpBytes);

            // OS/2 — pass through unchanged if present (required by Windows)
            byte[]? os2 = font.GetTableBytes("OS/2");
            if (os2 != null) AddTable(tables, "OS/2", (byte[])os2.Clone());

            AddTable(tables, "hmtx", hmtxBytes);
            AddTable(tables, "cmap", cmapBytes);

            // name, post — pass through unchanged
            byte[]? nameBytes = font.GetTableBytes("name");
            if (nameBytes != null) AddTable(tables, "name", (byte[])nameBytes.Clone());
            byte[]? postBytes = font.GetTableBytes("post");
            if (postBytes != null) AddTable(tables, "post", (byte[])postBytes.Clone());

            AddTable(tables, "loca", glyfLoca.LocaBytes);
            AddTable(tables, "glyf", glyfLoca.GlyfBytes);

            // Optional hinting / metrics tables — pass through unchanged.
            foreach (string tag in new[] { "cvt ", "fpgm", "prep", "gasp" })
            {
                byte[]? t = font.GetTableBytes(tag);
                if (t != null) AddTable(tables, tag, (byte[])t.Clone());
            }

            // 8. Serialise sfnt.
            return SerialiseSfnt(tables);
        }

        // -----------------------------------------------------------------------
        // sfnt serialisation with checksum computation
        // -----------------------------------------------------------------------

        private static byte[] SerialiseSfnt(List<(string tag, byte[] data)> tables)
        {
            int numTables = tables.Count;

            // sfnt offset table: 12 bytes.
            // Table directory: numTables × 16 bytes.
            int headerSize = 12 + numTables * 16;

            // Tables must start at 4-byte aligned offsets.
            // Compute per-table offsets.
            var tableOffsets = new uint[numTables];
            uint runningOffset = (uint)headerSize;
            for (int i = 0; i < numTables; i++)
            {
                tableOffsets[i] = runningOffset;
                uint len = (uint)tables[i].data.Length;
                // Pad to 4-byte boundary.
                uint paddedLen = (len + 3) & ~3u;
                runningOffset += paddedLen;
            }
            uint totalSize = runningOffset;

            var buf = new byte[totalSize];
            int p = 0;

            // --- sfnt offset table (12 bytes) ---
            WriteU32(buf, ref p, 0x00010000u);  // sfntVersion = TrueType

            // searchRange = (2^floor(log2(numTables))) × 16
            int pow = 1;
            while (pow * 2 <= numTables) pow *= 2;
            int searchRange16   = pow * 16;
            int entrySelector   = Log2(pow);
            int rangeShift      = numTables * 16 - searchRange16;

            WriteU16BE(buf, ref p, (ushort)numTables);
            WriteU16BE(buf, ref p, (ushort)searchRange16);
            WriteU16BE(buf, ref p, (ushort)entrySelector);
            WriteU16BE(buf, ref p, (ushort)rangeShift);

            // --- table directory (numTables × 16 bytes) ---
            // We'll fill in checksums after copying data.
            int dirStart = p;
            int[] checksumFieldOffsets = new int[numTables];
            for (int i = 0; i < numTables; i++)
            {
                byte[] tagBytes = Encoding.ASCII.GetBytes(tables[i].tag);
                Array.Copy(tagBytes, 0, buf, p, 4);
                p += 4;
                checksumFieldOffsets[i] = p;        // will write checksum here
                WriteU32(buf, ref p, 0u);            // checksum placeholder
                WriteU32(buf, ref p, tableOffsets[i]);
                WriteU32(buf, ref p, (uint)tables[i].data.Length);
            }

            // --- table data ---
            for (int i = 0; i < numTables; i++)
            {
                byte[] data = tables[i].data;
                Array.Copy(data, 0, buf, (int)tableOffsets[i], data.Length);
                // Padding bytes remain 0 (already zeroed).
            }

            // --- compute per-table checksums and write into directory ---
            for (int i = 0; i < numTables; i++)
            {
                uint cksum = TableChecksum(buf, (int)tableOffsets[i], tables[i].data.Length);
                int fOff = checksumFieldOffsets[i];
                buf[fOff]     = (byte)(cksum >> 24);
                buf[fOff + 1] = (byte)(cksum >> 16);
                buf[fOff + 2] = (byte)(cksum >> 8);
                buf[fOff + 3] = (byte)(cksum & 0xFF);
            }

            // --- compute whole-font checksum and store in head.checkSumAdjustment ---
            // head.checkSumAdjustment = 0xB1B0AFBA − (sum of ALL uint32s in the file).
            uint fontSum = 0;
            for (int i = 0; i + 3 < buf.Length; i += 4)
            {
                fontSum += (uint)((buf[i] << 24) | (buf[i + 1] << 16) | (buf[i + 2] << 8) | buf[i + 3]);
            }
            // Handle trailing 1–3 bytes if totalSize is not a multiple of 4.
            int remainder = buf.Length & 3;
            if (remainder > 0)
            {
                uint last = 0;
                int base_ = buf.Length - remainder;
                for (int k = 0; k < remainder; k++)
                    last |= (uint)buf[base_ + k] << (24 - k * 8);
                fontSum += last;
            }

            uint adj = 0xB1B0AFBAu - fontSum;

            // Locate the head table in the buffer to write checkSumAdjustment.
            int headTableIdx = -1;
            for (int i = 0; i < numTables; i++)
            {
                if (tables[i].tag == "head") { headTableIdx = i; break; }
            }
            if (headTableIdx >= 0)
            {
                int headOff = (int)tableOffsets[headTableIdx] + 8; // checkSumAdjustment at byte 8
                buf[headOff]     = (byte)(adj >> 24);
                buf[headOff + 1] = (byte)(adj >> 16);
                buf[headOff + 2] = (byte)(adj >> 8);
                buf[headOff + 3] = (byte)(adj & 0xFF);
            }

            return buf;
        }

        // -----------------------------------------------------------------------
        // Checksum helpers
        // -----------------------------------------------------------------------

        /// <summary>
        /// Compute the OpenType table checksum: sum of all uint32s (big-endian),
        /// padding the last partial uint32 with zero bytes on the right.
        /// </summary>
        private static uint TableChecksum(byte[] buf, int offset, int length)
        {
            uint sum = 0;
            int end = offset + length;
            for (int i = offset; i < end; i += 4)
            {
                uint word = 0;
                for (int k = 0; k < 4; k++)
                {
                    word <<= 8;
                    if (i + k < end)
                        word |= buf[i + k];
                }
                sum += word;
            }
            return sum;
        }

        // -----------------------------------------------------------------------
        // Utility
        // -----------------------------------------------------------------------

        private static byte[]? CloneTable(SfntFont font, string tag)
        {
            byte[]? raw = font.GetTableBytes(tag);
            if (raw is null) return null;
            var copy = new byte[raw.Length];
            Array.Copy(raw, copy, raw.Length);
            return copy;
        }

        private static void AddTable(List<(string, byte[])> list, string tag, byte[] data)
        {
            list.Add((tag, data));
        }

        private static void WriteBEU16(byte[] buf, int offset, ushort value)
        {
            buf[offset]     = (byte)(value >> 8);
            buf[offset + 1] = (byte)(value & 0xFF);
        }

        private static void WriteBEU32(byte[] buf, int offset, uint value)
        {
            buf[offset]     = (byte)(value >> 24);
            buf[offset + 1] = (byte)(value >> 16);
            buf[offset + 2] = (byte)(value >> 8);
            buf[offset + 3] = (byte)(value & 0xFF);
        }

        private static void WriteU32(byte[] buf, ref int pos, uint value)
        {
            buf[pos++] = (byte)(value >> 24);
            buf[pos++] = (byte)(value >> 16);
            buf[pos++] = (byte)(value >> 8);
            buf[pos++] = (byte)(value & 0xFF);
        }

        private static void WriteU16BE(byte[] buf, ref int pos, ushort value)
        {
            buf[pos++] = (byte)(value >> 8);
            buf[pos++] = (byte)(value & 0xFF);
        }

        private static int Log2(int n)
        {
            int r = 0;
            while (n > 1) { n >>= 1; r++; }
            return r;
        }
    }
}
