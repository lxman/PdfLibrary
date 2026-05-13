using System;

namespace Jp2Codec.Tier2
{
    /// <summary>
    /// Describes one code-block's contribution to a packet — which block
    /// (subband index + (x, y) inside the subband), how many segments the
    /// new coding passes split into (per the active code-block style), and
    /// the first-inclusion metadata. Each segment carries its own pass
    /// count and byte length; the total pass count for the contribution is
    /// the sum across segments.
    /// </summary>
    internal readonly struct CodeBlockContribution
    {
        public int SubbandIndex { get; }
        public int X { get; }
        public int Y { get; }
        public bool IsFirstInclusion { get; }
        public int ZeroBitPlanesIfFirst { get; }
        public ContributionSegment[] Segments { get; }

        public CodeBlockContribution(
            int subbandIndex, int x, int y,
            bool isFirstInclusion, int zeroBitPlanesIfFirst,
            ContributionSegment[] segments)
        {
            SubbandIndex = subbandIndex;
            X = x;
            Y = y;
            IsFirstInclusion = isFirstInclusion;
            ZeroBitPlanesIfFirst = zeroBitPlanesIfFirst;
            Segments = segments ?? throw new ArgumentNullException(nameof(segments));
        }

        /// <summary>Total new coding passes across all segments.</summary>
        public int TotalNewCodingPasses
        {
            get
            {
                var total = 0;
                for (var i = 0; i < Segments.Length; i++) total += Segments[i].PassCount;
                return total;
            }
        }

        /// <summary>Total body byte length across all segments.</summary>
        public int TotalBodyLength
        {
            get
            {
                var total = 0;
                for (var i = 0; i < Segments.Length; i++) total += Segments[i].ByteLength;
                return total;
            }
        }
    }
}
