using System;

namespace Jp2Codec.Tier2
{
    /// <summary>
    /// One subband worth of code-blocks within a precinct. Holds the per-
    /// block state plus the precinct's two tag trees for this subband
    /// (inclusion = "first layer the block appears in" and zero-bitplane =
    /// "missing most-significant bit-planes for the block").
    /// </summary>
    internal sealed class PrecinctSubband
    {
        public int CodeBlockColumnCount { get; }
        public int CodeBlockRowCount { get; }
        public CodeBlockState[,] CodeBlocks { get; }
        public TagTreeDecoder InclusionTree { get; }
        public TagTreeDecoder ZeroBitPlanesTree { get; }

        public PrecinctSubband(int codeBlockColumnCount, int codeBlockRowCount)
        {
            // 0×N or N×0 grids are legal — they happen on edge tiles where a
            // deep-level subband (e.g. HL_5) ends up entirely outside the
            // tile's reference-grid slice, so the precinct has no code-blocks
            // for that subband. The tag trees and per-block state are simply
            // empty; Tier-2 walks them as no-ops.
            if (codeBlockColumnCount < 0) throw new ArgumentOutOfRangeException(nameof(codeBlockColumnCount));
            if (codeBlockRowCount < 0) throw new ArgumentOutOfRangeException(nameof(codeBlockRowCount));

            CodeBlockColumnCount = codeBlockColumnCount;
            CodeBlockRowCount = codeBlockRowCount;

            var blocks = new CodeBlockState[codeBlockRowCount, codeBlockColumnCount];
            for (var y = 0; y < codeBlockRowCount; y++)
                for (var x = 0; x < codeBlockColumnCount; x++)
                    blocks[y, x] = new CodeBlockState();
            CodeBlocks = blocks;

            InclusionTree = new TagTreeDecoder(System.Math.Max(codeBlockColumnCount, 1),
                                                System.Math.Max(codeBlockRowCount, 1));
            ZeroBitPlanesTree = new TagTreeDecoder(System.Math.Max(codeBlockColumnCount, 1),
                                                    System.Math.Max(codeBlockRowCount, 1));
        }
    }
}
