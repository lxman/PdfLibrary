using System;
using System.Collections.Generic;
using FontParser.Tables.TtTables;
using FontParser.Tables.TtTables.Glyf;

namespace FontParser.Subsetting
{
    /// <summary>
    /// Rebuilds the <c>glyf</c> and <c>loca</c> tables for a TrueType subset defined
    /// by a <see cref="GlyphIdRemap"/>.
    ///
    /// Algorithm:
    ///  1. Iterate new GIDs 0..N-1 in order; for each, look up the old GID via
    ///     <see cref="GlyphIdRemap.NewToOld"/>.
    ///  2. Copy the raw glyph bytes from the original <c>glyf</c> table using the
    ///     original <c>loca</c> offsets.  Zero-length glyphs (loca[i]==loca[i+1])
    ///     are represented as zero-length entries in the new table.
    ///  3. For composite glyphs (negative numberOfContours in the glyph header), walk
    ///     the raw component chain and patch every <c>GlyphIndex</c> field via
    ///     <see cref="GlyphIdRemap.OldToNew"/>.
    ///  4. Pad each glyph to a 2-byte boundary (same alignment the spec requires and
    ///     which most real fonts already have).
    ///  5. Produce the <c>loca</c> table: numGlyphs+1 cumulative offsets; choose
    ///     short format (Offset16) if max offset fits in 0x1FFFE, else long (Offset32).
    ///
    /// Composite component walk — exact per spec (OpenType / TrueType glyf table):
    ///   Each component record:
    ///     flags       ushort
    ///     glyphIndex  ushort   ← the field we patch
    ///     args:       ARG_1_AND_2_ARE_WORDS (bit 0) → 2×int16 or 2×uint16 (4 bytes)
    ///                 else                          → 2×int8  or 2×uint8  (2 bytes)
    ///     scale:      WE_HAVE_A_SCALE (bit 3)       → 1×F2Dot14 (2 bytes)
    ///                 WE_HAVE_AN_X_AND_Y_SCALE (bit 6) → 2×F2Dot14 (4 bytes)
    ///                 WE_HAVE_A_TWO_BY_TWO (bit 7)  → 4×F2Dot14 (8 bytes)
    ///   MORE_COMPONENTS (bit 5): set → another component follows.
    ///   After the last component there MAY be instruction bytes
    ///   (WE_HAVE_INSTRUCTIONS flag on any component, followed by uint16 count + bytes);
    ///   we do not need to skip them because we stop once MORE_COMPONENTS is clear.
    /// </summary>
    public static class GlyfLocaRebuilder
    {
        // CompositeGlyphFlags bit positions (matches the project's CompositeGlyphFlags enum)
        private const ushort FlagArg1And2AreWords   = 1 << 0;   // bit 0
        private const ushort FlagWeHaveAScale        = 1 << 3;   // bit 3
        private const ushort FlagMoreComponents      = 1 << 5;   // bit 5
        private const ushort FlagWeHaveAnXAndYScale  = 1 << 6;   // bit 6
        private const ushort FlagWeHaveATwoByTwo     = 1 << 7;   // bit 7

        /// <summary>
        /// Result of a <c>glyf</c>/<c>loca</c> rebuild operation.
        /// </summary>
        public sealed class RebuildResult
        {
            /// <summary>New <c>glyf</c> table bytes.</summary>
            public byte[] GlyfBytes { get; }

            /// <summary>
            /// New <c>loca</c> table bytes.  Length is exactly
            /// (<see cref="NumGlyphs"/> + 1) × (2 or 4) depending on
            /// <see cref="UseShortLoca"/>.
            /// </summary>
            public byte[] LocaBytes { get; }

            /// <summary>Cumulative offsets array (numGlyphs+1 entries, uint).</summary>
            public uint[] LocaOffsets { get; }

            /// <summary>Number of retained glyphs.</summary>
            public int NumGlyphs { get; }

            /// <summary>True if the loca table uses the short (Offset16) format.</summary>
            public bool UseShortLoca { get; }

            internal RebuildResult(
                byte[] glyfBytes,
                byte[] locaBytes,
                uint[] locaOffsets,
                int numGlyphs,
                bool useShortLoca)
            {
                GlyfBytes    = glyfBytes;
                LocaBytes    = locaBytes;
                LocaOffsets  = locaOffsets;
                NumGlyphs    = numGlyphs;
                UseShortLoca = useShortLoca;
            }
        }

        /// <summary>
        /// Rebuild the <c>glyf</c> and <c>loca</c> tables for a subset defined by
        /// <paramref name="remap"/>.
        /// </summary>
        /// <param name="originalGlyfBytes">Raw bytes of the original <c>glyf</c> table.</param>
        /// <param name="originalLoca">Parsed <see cref="LocaTable"/> from the original font.</param>
        /// <param name="remap">GID mapping from <see cref="GlyphIdRemap"/>.</param>
        /// <param name="glyfTable">
        /// Parsed <see cref="GlyphTable"/> used to identify which glyphs are composite.
        /// May be null; if null all glyphs are assumed simple (no patching).
        /// </param>
        public static RebuildResult Rebuild(
            byte[] originalGlyfBytes,
            LocaTable originalLoca,
            GlyphIdRemap remap,
            GlyphTable? glyfTable)
        {
            if (originalGlyfBytes is null) throw new ArgumentNullException(nameof(originalGlyfBytes));
            if (originalLoca is null) throw new ArgumentNullException(nameof(originalLoca));
            if (remap is null) throw new ArgumentNullException(nameof(remap));

            int n = remap.Count;

            // --- Pass 1: copy + patch glyph bytes, record per-glyph lengths ----
            var glyphChunks = new byte[n][];
            for (var newGid = 0; newGid < n; newGid++)
            {
                ushort oldGid = remap.NewToOld[newGid];

                // Bounds-check: if old GID is beyond the loca table, treat as empty.
                if (oldGid + 1 >= originalLoca.Offsets.Length)
                {
                    glyphChunks[newGid] = Array.Empty<byte>();
                    continue;
                }

                uint srcStart = originalLoca.Offsets[oldGid];
                uint srcEnd   = originalLoca.Offsets[oldGid + 1];

                if (srcEnd <= srcStart)
                {
                    // Zero-length entry (e.g. whitespace glyph with no outline).
                    glyphChunks[newGid] = Array.Empty<byte>();
                    continue;
                }

                uint glyphLen = srcEnd - srcStart;
                if (srcStart + glyphLen > (uint)originalGlyfBytes.Length)
                    throw new InvalidOperationException(
                        $"Glyph {oldGid}: loca range [{srcStart},{srcEnd}) exceeds glyf table length {originalGlyfBytes.Length}.");

                var chunk = new byte[glyphLen];
                Array.Copy(originalGlyfBytes, srcStart, chunk, 0, glyphLen);

                // If this is a composite glyph, patch the component GlyphIndex fields.
                if (glyphLen >= 2)
                {
                    // numberOfContours is the first short in the glyph header.
                    var numberOfContours = (short)((chunk[0] << 8) | chunk[1]);
                    if (numberOfContours < 0) // composite
                    {
                        PatchCompositeComponents(chunk, remap.OldToNew);
                    }
                }

                glyphChunks[newGid] = chunk;
            }

            // --- Pass 2: build new glyf bytes (2-byte aligned) and loca offsets ---
            uint runningOffset = 0;
            var locaOffsets = new uint[n + 1];

            // Pre-compute total size with padding to allocate the output buffer once.
            uint totalSize = 0;
            for (var i = 0; i < n; i++)
            {
                var len = (uint)glyphChunks[i].Length;
                // Round up to 2-byte boundary (TrueType alignment requirement).
                uint paddedLen = (len + 1) & ~1u;
                totalSize += paddedLen;
            }

            var newGlyfBytes = new byte[totalSize];
            for (var newGid = 0; newGid < n; newGid++)
            {
                locaOffsets[newGid] = runningOffset;
                byte[] chunk = glyphChunks[newGid];
                if (chunk.Length > 0)
                {
                    Array.Copy(chunk, 0, newGlyfBytes, runningOffset, chunk.Length);
                    uint paddedLen = ((uint)chunk.Length + 1) & ~1u;
                    runningOffset += paddedLen;
                }
                // Zero-length: loca[newGid] == loca[newGid+1] (both point to same offset).
            }
            locaOffsets[n] = runningOffset;

            // --- Pass 3: encode loca table ---
            // Short format: each entry = offset/2 as uint16.  Max representable offset = 0x1FFFE.
            // Long format: each entry = offset as uint32.
            bool useShort = runningOffset <= 0x1FFFFEu;
            byte[] locaBytes;
            if (useShort)
            {
                locaBytes = new byte[(n + 1) * 2];
                for (var i = 0; i <= n; i++)
                {
                    var halfOffset = (ushort)(locaOffsets[i] / 2);
                    locaBytes[i * 2]     = (byte)(halfOffset >> 8);
                    locaBytes[i * 2 + 1] = (byte)(halfOffset & 0xFF);
                }
            }
            else
            {
                locaBytes = new byte[(n + 1) * 4];
                for (var i = 0; i <= n; i++)
                {
                    uint off = locaOffsets[i];
                    locaBytes[i * 4]     = (byte)(off >> 24);
                    locaBytes[i * 4 + 1] = (byte)(off >> 16);
                    locaBytes[i * 4 + 2] = (byte)(off >> 8);
                    locaBytes[i * 4 + 3] = (byte)(off & 0xFF);
                }
            }

            return new RebuildResult(newGlyfBytes, locaBytes, locaOffsets, n, useShort);
        }

        // -----------------------------------------------------------------
        // Composite component chain walker / GlyphIndex patcher
        // -----------------------------------------------------------------

        /// <summary>
        /// Walk the raw bytes of a composite glyph (starting at byte 0 = glyph header)
        /// and patch every component <c>GlyphIndex</c> field using <paramref name="oldToNew"/>.
        /// <para>
        /// The glyph header is 10 bytes (numberOfContours + bounding box).  After the
        /// header comes the first component record; more follow while the MORE_COMPONENTS
        /// flag (bit 5) is set on the preceding component.
        /// </para>
        /// </summary>
        private static void PatchCompositeComponents(
            byte[] glyphBytes,
            IReadOnlyDictionary<ushort, ushort> oldToNew)
        {
            // Skip the 10-byte glyph header.
            var pos = 10;

            while (pos + 4 <= glyphBytes.Length) // need at least flags + glyphIndex (4 bytes)
            {
                // Read flags (big-endian ushort at pos).
                ushort flags = ReadBEUShort(glyphBytes, pos);
                pos += 2;

                // Read and patch glyphIndex.
                ushort oldGid = ReadBEUShort(glyphBytes, pos);
                if (oldToNew.TryGetValue(oldGid, out ushort newGid))
                {
                    WriteBEUShort(glyphBytes, pos, newGid);
                }
                // If the component GID is not in the map it means the closure was
                // incomplete (shouldn't happen after GlyphClosure.Compute) but we leave
                // the original value rather than writing garbage.
                pos += 2;

                // Skip arguments.
                if ((flags & FlagArg1And2AreWords) != 0)
                    pos += 4; // two int16 or uint16
                else
                    pos += 2; // two int8 or uint8

                // Skip transformation data.
                if ((flags & FlagWeHaveATwoByTwo) != 0)
                    pos += 8;        // 4 × F2Dot14
                else if ((flags & FlagWeHaveAnXAndYScale) != 0)
                    pos += 4;        // 2 × F2Dot14
                else if ((flags & FlagWeHaveAScale) != 0)
                    pos += 2;        // 1 × F2Dot14

                // Stop if MORE_COMPONENTS is not set.
                if ((flags & FlagMoreComponents) == 0)
                    break;
            }
        }

        // -----------------------------------------------------------------
        // Inline big-endian helpers (no allocation)
        // -----------------------------------------------------------------

        private static ushort ReadBEUShort(byte[] buf, int offset) =>
            (ushort)((buf[offset] << 8) | buf[offset + 1]);

        private static void WriteBEUShort(byte[] buf, int offset, ushort value)
        {
            buf[offset]     = (byte)(value >> 8);
            buf[offset + 1] = (byte)(value & 0xFF);
        }
    }
}
