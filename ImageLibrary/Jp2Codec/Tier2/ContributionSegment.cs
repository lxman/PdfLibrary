namespace Jp2Codec.Tier2
{
    /// <summary>
    /// One terminated byte segment of a code-block contribution. Default
    /// style yields one segment per contribution; TERMALL splits every pass
    /// into its own segment; LAZY (Annex D.6) alternates raw and MQ
    /// segments from the fifth non-zero bit-plane onward. Each segment
    /// carries its own byte length (signalled separately in the packet
    /// header per B.10.7.1) and its own pass count.
    /// </summary>
    internal readonly struct ContributionSegment
    {
        /// <summary>Number of coding passes this segment carries.</summary>
        public int PassCount { get; }

        /// <summary>Byte length of the segment in the packet body.</summary>
        public int ByteLength { get; }

        /// <summary>True if the segment is raw (un-MQ-coded) — only happens under LAZY.</summary>
        public bool IsRaw { get; }

        public ContributionSegment(int passCount, int byteLength, bool isRaw)
        {
            PassCount = passCount;
            ByteLength = byteLength;
            IsRaw = isRaw;
        }
    }
}
