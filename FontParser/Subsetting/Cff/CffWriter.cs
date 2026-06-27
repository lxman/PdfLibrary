using System;
using System.Collections.Generic;
using System.Globalization;

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
            if (count > ushort.MaxValue)
                throw new ArgumentException(
                    $"A CFF INDEX cannot hold more than {ushort.MaxValue} entries (got {count}).", nameof(entries));
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
                var off = (uint)offsets[i];
                for (int b = offSize - 1; b >= 0; b--)
                    buf[p++] = (byte)(off >> (b * 8));
            }

            for (var i = 0; i < count; i++)   // data, concatenated in order
                foreach (byte by in entries[i])
                    buf[p++] = by;

            return buf;
        }

        /// <summary>Byte offset of entry <paramref name="entryIndex"/>'s data within the INDEX that
        /// <see cref="WriteIndex"/> would produce for the given entry lengths. Lets callers locate
        /// fixed-width offset placeholders inside a wrapped multi-entry INDEX (e.g. the FDArray).</summary>
        public static int IndexEntryDataOffset(IReadOnlyList<int> entryLengths, int entryIndex)
        {
            int count = entryLengths.Count;
            var maxOffset = 1;
            for (var i = 0; i < count; i++) maxOffset += entryLengths[i];
            int offSize = maxOffset <= 0xFF ? 1 : maxOffset <= 0xFFFF ? 2 : maxOffset <= 0xFFFFFF ? 3 : 4;

            int dataStart = 2 + 1 + (count + 1) * offSize; // count + offSize + offset array
            for (var i = 0; i < entryIndex; i++) dataStart += entryLengths[i];
            return dataStart;
        }

        /// <summary>Encodes an integer as a CFF DICT operand in the shortest form (mirrors <c>Calc.Integer</c>).</summary>
        public static byte[] EncodeInteger(int value)
        {
            if (value is >= -107 and <= 107)
                return new[] { (byte)(value + 139) };
            if (value is >= 108 and <= 1131)
            {
                int w = value - 108;
                return new[] { (byte)(247 + (w >> 8)), (byte)(w & 0xFF) };
            }
            if (value is >= -1131 and <= -108)
            {
                int w = -value - 108;
                return new[] { (byte)(251 + (w >> 8)), (byte)(w & 0xFF) };
            }
            if (value is >= short.MinValue and <= short.MaxValue)
                return new[] { (byte)0x1C, (byte)(value >> 8), (byte)value };
            return new[] { (byte)0x1D, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };
        }

        /// <summary>Encodes an integer in the FIXED 5-byte form (0x1D + int32 BE). Used for DICT offset
        /// operands so the DICT size is invariant to the offset value, enabling single-pass layout.</summary>
        public static byte[] EncodeFixedOffset(int value) =>
            new[] { (byte)0x1D, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };

        /// <summary>Encodes a real as a CFF DICT operand (0x1E + 4-bit nibbles, no exponent; mirrors <c>Calc.Double</c>).</summary>
        public static byte[] EncodeReal(double value)
        {
            var s = value.ToString("0.0###############", CultureInfo.InvariantCulture);
            var nibbles = new List<byte>();
            foreach (char c in s)
            {
                if (c == '-') nibbles.Add(0xE);
                else if (c == '.') nibbles.Add(0xA);
                else if (c is >= '0' and <= '9') nibbles.Add((byte)(c - '0'));
            }
            nibbles.Add(0xF);                          // end marker
            if ((nibbles.Count & 1) == 1) nibbles.Add(0xF); // pad to a full byte

            var bytes = new List<byte>(1 + nibbles.Count / 2) { 0x1E };
            for (var i = 0; i < nibbles.Count; i += 2)
                bytes.Add((byte)((nibbles[i] << 4) | nibbles[i + 1]));
            return bytes.ToArray();
        }

        /// <summary>Encodes a DICT operator: 1 byte, or 2 bytes for escaped (0x0Cxx) operators.</summary>
        public static byte[] EncodeOperator(int operatorCode) =>
            operatorCode > 0xFF
                ? new[] { (byte)(operatorCode >> 8), (byte)(operatorCode & 0xFF) }
                : new[] { (byte)operatorCode };
    }
}
