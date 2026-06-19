using System;
using System.Collections.Generic;

namespace FontParser.Subsetting.Cff
{
    /// <summary>
    /// Low-level CFF (Compact Font Format) wire-format encoders used by the CFF subsetter.
    /// Round-trips against the existing reader (<see cref="FontParser.Tables.Cff.Type1.Type1Index"/>).
    /// </summary>
    public static class CffWriter
    {
        /// <summary>
        /// Encodes a CFF INDEX (count, offSize, offset array, data) from the given entries.
        /// An empty entry list yields the 2-byte empty INDEX (count = 0, no offSize).
        /// </summary>
        public static byte[] WriteIndex(IReadOnlyList<IReadOnlyList<byte>> entries)
        {
            int count = entries.Count;
            if (count == 0)
                return new byte[] { 0, 0 }; // empty INDEX: count = 0, no offSize/offsets/data

            // 1-based offsets relative to the byte before the data block. offset[0] = 1.
            var offsets = new int[count + 1];
            offsets[0] = 1;
            for (var i = 0; i < count; i++)
                offsets[i + 1] = offsets[i] + entries[i].Count;

            int dataLen = offsets[count] - 1;
            int maxOffset = offsets[count];
            int offSize = maxOffset <= 0xFF ? 1
                        : maxOffset <= 0xFFFF ? 2
                        : maxOffset <= 0xFFFFFF ? 3
                        : 4;

            int headerLen = 2 + 1 + (count + 1) * offSize; // count(2) + offSize(1) + offsets
            var buf = new byte[headerLen + dataLen];
            var p = 0;

            buf[p++] = (byte)(count >> 8);   // count, big-endian u16
            buf[p++] = (byte)(count & 0xFF);
            buf[p++] = (byte)offSize;

            for (var i = 0; i <= count; i++)  // offset array, big-endian, offSize bytes each
            {
                uint off = (uint)offsets[i];
                for (int b = offSize - 1; b >= 0; b--)
                    buf[p++] = (byte)(off >> (b * 8));
            }

            for (var i = 0; i < count; i++)   // data, concatenated in order
                foreach (byte by in entries[i])
                    buf[p++] = by;

            return buf;
        }
    }
}
