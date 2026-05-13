using System;

namespace Jp2Codec.Tier2
{
    /// <summary>
    /// A precinct as seen by the Tier-2 packet header parser: a fixed list
    /// of subbands at a single resolution level. Resolution 0 carries one
    /// subband (LL); resolutions r &gt; 0 carry three (HL, LH, HH in that
    /// order).
    /// </summary>
    internal sealed class Precinct
    {
        public PrecinctSubband[] Subbands { get; }

        public Precinct(PrecinctSubband[] subbands)
        {
            if (subbands is null) throw new ArgumentNullException(nameof(subbands));
            if (subbands.Length == 0)
                throw new ArgumentException("Precinct must have at least one subband.", nameof(subbands));
            Subbands = subbands;
        }
    }
}
