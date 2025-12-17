namespace ImageLibrary.Jbig2;

/// <summary>
/// Parses JBIG2 segment headers according to T.88 Section 7.2.
/// </summary>
internal static class SegmentHeaderParser
{
    /// <summary>
    /// Parses a segment header from the reader.
    /// </summary>
    public static SegmentHeader Parse(BitReader reader, Jbig2DecoderOptions? options = null)
    {
        options ??= Jbig2DecoderOptions.Default;

        // Validate we have minimum bytes for header
        if (reader.RemainingBytes < 11)
            throw new Jbig2DataException($"Insufficient data for segment header: need 11 bytes, have {reader.RemainingBytes}");

        // 7.2.2 Segment number (4 bytes)
        uint segmentNumber = reader.ReadUInt32BigEndian();

        // 7.2.3 Segment header flags (1 byte)
        byte flags = reader.ReadByte();
        var segmentType = (SegmentType)(flags & 0x3F);
        bool pageAssociationIs4Bytes = (flags & 0x40) != 0;
        bool deferredNonRetain = (flags & 0x20) != 0;

        // 7.2.4 Referred-to segment count and retention flags
        byte refCountByte = reader.ReadByte();
        int refCountIndicator = (refCountByte >> 5) & 0x07;

        int referredToCount;
        byte[] retentionFlags;

        if (refCountIndicator <= 4)
        {
            // Short form: count is in bits 5-7
            referredToCount = refCountIndicator;
            // Retention flags are in bits 0-4 of this byte (up to 5 segments)
            retentionFlags = [(byte)(refCountByte & 0x1F)];
        }
        else if (refCountIndicator == 7)
        {
            // Long form: count follows in 4 bytes
            // Validate we have enough bytes
            if (reader.RemainingBytes < 3)
                throw new Jbig2DataException("Insufficient data for long-form segment reference count");

            // First read remaining 3 bytes of the 4-byte count
            var countHigh = (uint)(refCountByte & 0x1F);
            byte b1 = reader.ReadByte();
            byte b2 = reader.ReadByte();
            byte b3 = reader.ReadByte();
            referredToCount = (int)((countHigh << 24) | (uint)(b1 << 16) | (uint)(b2 << 8) | b3);

            // Validate referred-to count
            if (referredToCount < 0)
                throw new Jbig2DataException($"Invalid referred-to segment count (overflow): {referredToCount}");
            if (referredToCount > options.MaxReferredSegments)
                throw new Jbig2ResourceException($"Referred-to segment count {referredToCount} exceeds limit {options.MaxReferredSegments}");

            // Retention flags follow (ceil(referredToCount + 1) / 8 bytes)
            int retentionFlagBytes = (referredToCount + 8) / 8;
            if (reader.RemainingBytes < retentionFlagBytes)
                throw new Jbig2DataException($"Insufficient data for retention flags: need {retentionFlagBytes}, have {reader.RemainingBytes}");
            retentionFlags = reader.ReadBytes(retentionFlagBytes);
        }
        else
        {
            throw new Jbig2DataException($"Invalid referred-to count indicator: {refCountIndicator}");
        }

        // 7.2.5 Referred-to segment numbers
        var referredToSegments = new uint[referredToCount];

        // Segment number size depends on this segment's number
        int segmentNumberSize = segmentNumber <= 256 ? 1 : (segmentNumber <= 65536 ? 2 : 4);

        // Validate we have enough bytes for all segment references
        int refBytesNeeded = referredToCount * segmentNumberSize;
        if (reader.RemainingBytes < refBytesNeeded)
            throw new Jbig2DataException($"Insufficient data for segment references: need {refBytesNeeded}, have {reader.RemainingBytes}");

        for (var i = 0; i < referredToCount; i++)
        {
            referredToSegments[i] = segmentNumberSize switch
            {
                1 => reader.ReadByte(),
                2 => reader.ReadUInt16BigEndian(),
                4 => reader.ReadUInt32BigEndian(),
                _ => throw new Jbig2DataException($"Invalid segment number size: {segmentNumberSize}")
            };
        }

        // 7.2.6 Page association
        int pageAssocBytes = pageAssociationIs4Bytes ? 4 : 1;
        if (reader.RemainingBytes < pageAssocBytes)
            throw new Jbig2DataException($"Insufficient data for page association: need {pageAssocBytes}, have {reader.RemainingBytes}");

        uint pageAssociation;
        if (pageAssociationIs4Bytes)
        {
            pageAssociation = reader.ReadUInt32BigEndian();
        }
        else
        {
            pageAssociation = reader.ReadByte();
        }

        // 7.2.7 Segment data length (4 bytes)
        if (reader.RemainingBytes < 4)
            throw new Jbig2DataException($"Insufficient data for segment data length: need 4, have {reader.RemainingBytes}");

        uint dataLength = reader.ReadUInt32BigEndian();

        // Determine retain flag from retention flags
        // The retain flag for this segment is bit (referredToCount) in the retention flags
        bool retainFlag = !deferredNonRetain;

        return new SegmentHeader
        {
            SegmentNumber = segmentNumber,
            Type = segmentType,
            RetainFlag = retainFlag,
            PageAssociation = pageAssociation,
            ReferredToSegments = referredToSegments,
            DataLength = dataLength
        };
    }

    /// <summary>
    /// Checks if the data starts with a JBIG2 file header.
    /// </summary>
    public static bool HasFileHeader(byte[] data)
    {
        // JBIG2 file header magic: 0x97 0x4A 0x42 0x32 0x0D 0x0A 0x1A 0x0A
        if (data.Length < 8)
            return false;

        return data[0] == 0x97 &&
               data[1] == 0x4A &&
               data[2] == 0x42 &&
               data[3] == 0x32 &&
               data[4] == 0x0D &&
               data[5] == 0x0A &&
               data[6] == 0x1A &&
               data[7] == 0x0A;
    }

    /// <summary>
    /// Parses JBIG2 file header if present.
    /// Returns the byte offset after the file header, or 0 if no file header.
    /// </summary>
    public static (int offset, bool sequential, uint pageCount) ParseFileHeader(byte[] data)
    {
        if (!HasFileHeader(data))
            return (0, true, 0);

        if (data.Length < 9)
            throw new Jbig2DataException("File header truncated: missing flags byte");

        // Skip magic bytes
        var offset = 8;

        // File header flags (1 byte)
        byte flags = data[offset++];
        bool sequential = (flags & 0x01) == 0; // Bit 0: 0 = sequential, 1 = random-access
        bool hasKnownPageCount = (flags & 0x02) == 0; // Bit 1: 0 = known page count follows

        // Number of pages (4 bytes, if known)
        uint pageCount = 0;
        if (hasKnownPageCount)
        {
            if (data.Length < offset + 4)
                throw new Jbig2DataException("File header truncated: missing page count");

            pageCount = (uint)((data[offset] << 24) | (data[offset + 1] << 16) |
                               (data[offset + 2] << 8) | data[offset + 3]);
            offset += 4;
        }

        return (offset, sequential, pageCount);
    }
}
