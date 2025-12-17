namespace ImageLibrary.Jbig2;

/// <summary>
/// Represents a JBIG2 segment header as defined in T.88 Section 7.2.
/// </summary>
internal sealed class SegmentHeader
{
    /// <summary>
    /// Segment number (7.2.2).
    /// </summary>
    public uint SegmentNumber { get; init; }

    /// <summary>
    /// Segment type (7.2.3).
    /// </summary>
    public SegmentType Type { get; init; }

    /// <summary>
    /// Whether this segment's data should be retained for reference by other segments.
    /// </summary>
    public bool RetainFlag { get; init; }

    /// <summary>
    /// Page association - which page this segment belongs to (0 = global).
    /// </summary>
    public uint PageAssociation { get; init; }

    /// <summary>
    /// Segment numbers that this segment refers to.
    /// </summary>
    public uint[] ReferredToSegments { get; init; } = [];

    /// <summary>
    /// Length of the segment data in bytes.
    /// </summary>
    public uint DataLength { get; init; }

    /// <summary>
    /// Whether data length is unknown (indicated by 0xFFFFFFFF).
    /// </summary>
    public bool IsDataLengthUnknown => DataLength == 0xFFFFFFFF;

    public override string ToString() =>
        $"Segment {SegmentNumber}: {Type}, Page {PageAssociation}, DataLen {DataLength}, Refs [{string.Join(",", ReferredToSegments)}]";
}
