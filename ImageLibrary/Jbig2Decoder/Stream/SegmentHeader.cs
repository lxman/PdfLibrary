namespace Jbig2Decoder.Stream
{
    /// <summary>
    /// Parsed JBIG2 segment header (T.88 §7.2).
    ///
    /// <para><see cref="DataLength"/> is the byte length of the segment body
    /// that follows the header. The spec sentinel <c>0xFFFFFFFF</c> means
    /// "unknown length, scan for an end-of-stripe marker" — currently
    /// surfaced as <c>uint.MaxValue</c> for callers to handle (or fail).</para>
    /// </summary>
    internal sealed class SegmentHeader
    {
        public uint Number;
        public byte Flags;
        public uint[]? ReferredToSegments;
        public uint PageAssociation;
        public uint DataLength;
        public int HeaderLengthBytes;     // bytes consumed by the header itself (for stream stepping)

        public SegmentType Type => (SegmentType)(Flags & 0x3F);
        public bool RetainBit => (Flags & 0x40) != 0;
        public bool LongPageAssociation => (Flags & 0x80) != 0;
        public bool DataLengthDeferred => DataLength == uint.MaxValue;
    }
}
