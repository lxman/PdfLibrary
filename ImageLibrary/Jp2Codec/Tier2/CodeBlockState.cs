namespace Jp2Codec.Tier2
{
    /// <summary>
    /// Mutable per-code-block state that persists across packets for a single
    /// (resolution, component, precinct, subband, block) tuple. The Tier-2
    /// packet header parser advances this state every time it sees the block
    /// contribute.
    /// </summary>
    internal sealed class CodeBlockState
    {
        /// <summary>
        /// Length-prefix bit count for body lengths. Initialised to 3 per
        /// ISO/IEC 15444-1 B.10.7; the encoder may increment it via the
        /// comma code preceding any contribution.
        /// </summary>
        public int Lblock { get; set; } = 3;

        /// <summary>
        /// True once the block has been included in any packet. After first
        /// inclusion, subsequent packets signal contribution via a single
        /// bit rather than the inclusion tag tree.
        /// </summary>
        public bool Included { get; set; }

        /// <summary>
        /// Count of zero (missing) most-significant bit-planes for the
        /// block. Only meaningful once <see cref="Included"/> is true;
        /// captured from the precinct's zero-bitplane tag tree on first
        /// inclusion.
        /// </summary>
        public int ZeroBitPlanes { get; set; }

        /// <summary>
        /// Sum of coding passes contributed by the block across all packets
        /// that have been parsed so far.
        /// </summary>
        public int CompletedPasses { get; set; }
    }
}
