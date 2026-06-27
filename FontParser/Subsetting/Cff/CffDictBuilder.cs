using System;
using System.Collections.Generic;

namespace FontParser.Subsetting.Cff
{
    /// <summary>
    /// Accumulates CFF DICT entries (operands followed by an operator) into a byte buffer.
    /// Offset operators use a fixed 5-byte placeholder whose value is backfilled after layout via
    /// <see cref="PatchOffset"/>, so the DICT's size is known before section offsets are computed.
    /// </summary>
    public sealed class CffDictBuilder
    {
        private readonly List<byte> _bytes = new();

        /// <summary>Current encoded length (the DICT's size, stable once all operators are added).</summary>
        public int Length => _bytes.Count;

        /// <summary>Append an operator preceded by integer/real operands (integral values use the integer form).</summary>
        public CffDictBuilder Add(int operatorCode, params double[] operands)
        {
            foreach (double o in operands)
                _bytes.AddRange(o == Math.Floor(o) && o is >= int.MinValue and <= int.MaxValue
                    ? CffWriter.EncodeInteger((int)o)
                    : CffWriter.EncodeReal(o));
            _bytes.AddRange(CffWriter.EncodeOperator(operatorCode));
            return this;
        }

        /// <summary>Append an operator with a single fixed-width offset operand (placeholder 0). Returns the
        /// buffer position of the 4-byte big-endian value for later <see cref="PatchOffset"/>.</summary>
        public int AddOffset(int operatorCode)
        {
            _bytes.AddRange(CffWriter.EncodeFixedOffset(0));
            int valuePos = _bytes.Count - 4;
            _bytes.AddRange(CffWriter.EncodeOperator(operatorCode));
            return valuePos;
        }

        /// <summary>Append an operator with two fixed-width offset operands (e.g. Private = size, offset).
        /// Returns the buffer positions of both 4-byte values, in operand order.</summary>
        public (int firstPos, int secondPos) AddOffsetPair(int operatorCode)
        {
            _bytes.AddRange(CffWriter.EncodeFixedOffset(0));
            int firstPos = _bytes.Count - 4;
            _bytes.AddRange(CffWriter.EncodeFixedOffset(0));
            int secondPos = _bytes.Count - 4;
            _bytes.AddRange(CffWriter.EncodeOperator(operatorCode));
            return (firstPos, secondPos);
        }

        /// <summary>Append a pre-encoded DICT entry (operands + operator) verbatim.</summary>
        public CffDictBuilder AppendRaw(byte[] entryBytes)
        {
            _bytes.AddRange(entryBytes);
            return this;
        }

        /// <summary>Backfill a 4-byte big-endian value at a position returned by <see cref="AddOffset"/>/<see cref="AddOffsetPair"/>.</summary>
        public void PatchOffset(int valuePos, int value)
        {
            _bytes[valuePos]     = (byte)(value >> 24);
            _bytes[valuePos + 1] = (byte)(value >> 16);
            _bytes[valuePos + 2] = (byte)(value >> 8);
            _bytes[valuePos + 3] = (byte)value;
        }

        public byte[] Build() => _bytes.ToArray();
    }
}
