namespace Jp2Codec.Wavelet
{
    /// <summary>
    /// One decomposition level's HL/LH/HH subbands plus the canvas-start
    /// parities of the LL band that the level-step produces (i.e., the
    /// parent canvas parities for the 2D_SR procedure at this level).
    ///
    /// <para>
    /// For an N_L-level decomposition: index 0 in a <c>WaveletLevel53[]</c>
    /// holds level 1 (the finest detail), index N_L-1 holds level N_L
    /// (the coarsest).
    /// </para>
    /// </summary>
    internal sealed class WaveletLevel53
    {
        public int[,] HL { get; }
        public int[,] LH { get; }
        public int[,] HH { get; }

        /// <summary>Parity of the parent (lower-level) LL canvas-start column.</summary>
        public int U0ParityAtParent { get; }

        /// <summary>Parity of the parent (lower-level) LL canvas-start row.</summary>
        public int V0ParityAtParent { get; }

        public WaveletLevel53(int[,] hl, int[,] lh, int[,] hh, int u0ParityAtParent, int v0ParityAtParent)
        {
            HL = hl;
            LH = lh;
            HH = hh;
            U0ParityAtParent = u0ParityAtParent;
            V0ParityAtParent = v0ParityAtParent;
        }
    }

    /// <summary>9/7 float-coefficient counterpart to <see cref="WaveletLevel53"/>.</summary>
    internal sealed class WaveletLevel97
    {
        public float[,] HL { get; }
        public float[,] LH { get; }
        public float[,] HH { get; }
        public int U0ParityAtParent { get; }
        public int V0ParityAtParent { get; }

        public WaveletLevel97(float[,] hl, float[,] lh, float[,] hh, int u0ParityAtParent, int v0ParityAtParent)
        {
            HL = hl;
            LH = lh;
            HH = hh;
            U0ParityAtParent = u0ParityAtParent;
            V0ParityAtParent = v0ParityAtParent;
        }
    }
}
