using System;

namespace Jbig2Decoder.Stream
{
    internal static class SegmentParser
    {
        /// <summary>
        /// Parse one segment header starting at <paramref name="offset"/>.
        /// Returns the parsed header (including its byte length, see
        /// <see cref="SegmentHeader.HeaderLengthBytes"/>).
        /// Throws <see cref="InvalidOperationException"/> on truncation or
        /// malformed input.
        /// </summary>
        public static SegmentHeader Parse(byte[] buf, int offset)
        {
            if (buf is null) throw new ArgumentNullException(nameof(buf));
            if (offset < 0 || offset > buf.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if (offset + 11 > buf.Length)
                throw new InvalidOperationException("Truncated segment header (need at least 11 bytes)");

            var h = new SegmentHeader();

            // 7.2.2 Segment number (4 bytes BE).
            h.Number = BigEndian.U32(buf, offset);

            // 7.2.3 Segment header flags (1 byte).
            h.Flags = buf[offset + 4];

            // 7.2.4 Retention flags + referred-to segment count.
            byte rtscarf = buf[offset + 5];
            uint refCount;
            int o; // running offset within this header
            if ((rtscarf & 0xE0) == 0xE0)
            {
                // Long form (T.88 §7.2.4): count packed into the low 29 bits
                // of the 4 bytes starting at offset+5 (the rtscarf byte itself
                // contributes the high 3 bits), followed by ceil((count+1)/8)
                // bytes of retention flags. NB jbig2dec's open-source parser
                // computes the retention-byte count as `(count+1)/8` integer
                // division, which floors instead of ceiling and is wrong for
                // any count where (count+1) is not a multiple of 8 (e.g. the
                // jbig2-tests-pdf bitmap-symbol-manyrefs stream with count=5
                // needs 1 retention byte, not 0). We use the proper ceiling
                // form so the rest of the header parses on the right offset.
                if (offset + 5 + 4 > buf.Length)
                    throw new InvalidOperationException("Truncated long-form referred-to count");
                uint rtscarfLong = BigEndian.U32(buf, offset + 5);
                refCount = rtscarfLong & 0x1FFFFFFFu;
                o = 5 + 4 + (int)((refCount + 8) / 8);
            }
            else
            {
                refCount = (uint)(rtscarf >> 5);
                o = 5 + 1;
            }

            // 7.2.5 Referred-to segment numbers — width depends on this segment's number.
            int refSize = h.Number <= 256 ? 1 : h.Number <= 65536 ? 2 : 4;
            int paSize = (h.Flags & 0x40) != 0 ? 4 : 1;
            int totalLen = o + (int)refCount * refSize + paSize + 4;
            if (offset + totalLen > buf.Length)
                throw new InvalidOperationException("Truncated segment header body");

            if (refCount > 0)
            {
                var refs = new uint[refCount];
                for (var i = 0; i < refCount; i++)
                {
                    refs[i] = refSize switch
                    {
                        1 => buf[offset + o],
                        2 => BigEndian.U16(buf, offset + o),
                        _ => BigEndian.U32(buf, offset + o),
                    };
                    o += refSize;
                }
                h.ReferredToSegments = refs;
            }

            // 7.2.6 Page association (1 or 4 bytes).
            if (paSize == 4)
            {
                h.PageAssociation = BigEndian.U32(buf, offset + o);
                o += 4;
            }
            else
            {
                h.PageAssociation = buf[offset + o];
                o += 1;
            }

            // 7.2.7 Segment data length (4 bytes; 0xFFFFFFFF = unknown / deferred).
            h.DataLength = BigEndian.U32(buf, offset + o);
            o += 4;

            h.HeaderLengthBytes = o;
            return h;
        }
    }
}
