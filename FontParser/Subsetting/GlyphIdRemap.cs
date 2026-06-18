using System;
using System.Collections.Generic;

namespace FontParser.Subsetting
{
    /// <summary>
    /// Builds a contiguous new-GID assignment from the sorted closure produced by
    /// <see cref="GlyphClosure.Compute"/>.  Old GID 0 (the .notdef glyph) is always
    /// mapped to new GID 0 when it is present in the retained set.
    ///
    /// Usage:
    ///   var remap = new GlyphIdRemap(closureArray);
    ///   ushort newGid = remap.OldToNew[oldGid];
    ///   ushort oldGid = remap.NewToOld[newGid];
    /// </summary>
    public sealed class GlyphIdRemap
    {
        /// <summary>Old GID → new GID (contiguous, 0-based).</summary>
        public IReadOnlyDictionary<ushort, ushort> OldToNew { get; }

        /// <summary>New GID → old GID (inverse of OldToNew).</summary>
        public IReadOnlyList<ushort> NewToOld { get; }

        /// <summary>Number of retained glyphs (= new GID count).</summary>
        public int Count { get; }

        /// <summary>
        /// Initialise from the sorted closure array returned by
        /// <see cref="GlyphClosure.Compute"/>.  The array must be sorted ascending
        /// (it always is when produced by GlyphClosure.Compute).
        /// </summary>
        public GlyphIdRemap(ushort[] sortedRetainedGids)
        {
            if (sortedRetainedGids is null) throw new ArgumentNullException(nameof(sortedRetainedGids));

            Count = sortedRetainedGids.Length;
            var oldToNew = new Dictionary<ushort, ushort>(Count);
            var newToOld = new ushort[Count];

            for (int i = 0; i < sortedRetainedGids.Length; i++)
            {
                ushort oldGid = sortedRetainedGids[i];
                var newGid = (ushort)i;
                oldToNew[oldGid] = newGid;
                newToOld[i] = oldGid;
            }

            OldToNew = oldToNew;
            NewToOld = newToOld;
        }
    }
}
