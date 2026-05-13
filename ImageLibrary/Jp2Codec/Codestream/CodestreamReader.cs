using System;
using System.IO;

namespace Jp2Codec.Codestream
{
    /// <summary>
    /// Byte/big-endian primitive over a J2K codestream extent. Header data
    /// outside packet streams contains no byte-stuffing — Lsiz, Xsiz, COD/QCD
    /// payload bytes etc. are read raw. The bit-level stuff-bit conventions
    /// that apply inside packet headers and inside the MQ-coded packet body
    /// are handled by separate readers further down the stack.
    ///
    /// Convention: Position is reported relative to the codestream start the
    /// caller passed in, not the absolute byte offset in the host buffer. That
    /// keeps diagnostics framed in spec coordinates (where position 0 is the
    /// SOC marker).
    /// </summary>
    internal sealed class CodestreamReader
    {
        private readonly byte[] _buffer;
        private readonly int _start;
        private readonly int _end;
        private int _pos;

        public CodestreamReader(byte[] buffer, int offset, int length)
        {
            if (buffer is null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (length < 0 || offset + length > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(length));

            _buffer = buffer;
            _start = offset;
            _end = offset + length;
            _pos = offset;
        }

        public CodestreamReader(byte[] buffer) : this(buffer, 0, buffer?.Length ?? 0) { }

        /// <summary>Cursor position relative to codestream start (0 = first byte).</summary>
        public int Position => _pos - _start;

        /// <summary>Bytes remaining from the cursor to the end of the extent.</summary>
        public int Remaining => _end - _pos;

        /// <summary>True once the cursor has consumed the entire extent.</summary>
        public bool IsAtEnd => _pos >= _end;

        /// <summary>Total length of the codestream extent.</summary>
        public int Length => _end - _start;

        /// <summary>Read a single byte; throws if the cursor is at end.</summary>
        public byte ReadByte()
        {
            if (_pos >= _end) throw EndOfStream(1);
            return _buffer[_pos++];
        }

        /// <summary>Read a big-endian unsigned 16-bit value (2 bytes).</summary>
        public ushort ReadUInt16BigEndian()
        {
            if (_pos + 1 >= _end) throw EndOfStream(2);
            ushort v = (ushort)((_buffer[_pos] << 8) | _buffer[_pos + 1]);
            _pos += 2;
            return v;
        }

        /// <summary>Read a big-endian unsigned 32-bit value (4 bytes).</summary>
        public uint ReadUInt32BigEndian()
        {
            if (_pos + 3 >= _end) throw EndOfStream(4);
            uint v = ((uint)_buffer[_pos] << 24)
                   | ((uint)_buffer[_pos + 1] << 16)
                   | ((uint)_buffer[_pos + 2] << 8)
                   | _buffer[_pos + 3];
            _pos += 4;
            return v;
        }

        /// <summary>Read <paramref name="count"/> bytes into a fresh array.</summary>
        public byte[] ReadBytes(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (_pos + count > _end) throw EndOfStream(count);
            var dst = new byte[count];
            Buffer.BlockCopy(_buffer, _pos, dst, 0, count);
            _pos += count;
            return dst;
        }

        /// <summary>Read <paramref name="count"/> bytes as a span over the underlying buffer (no copy).</summary>
        public ReadOnlySpan<byte> ReadSpan(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (_pos + count > _end) throw EndOfStream(count);
            var span = new ReadOnlySpan<byte>(_buffer, _pos, count);
            _pos += count;
            return span;
        }

        /// <summary>Peek a big-endian uint16 without advancing the cursor.</summary>
        public ushort PeekUInt16BigEndian()
        {
            if (_pos + 1 >= _end) throw EndOfStream(2);
            return (ushort)((_buffer[_pos] << 8) | _buffer[_pos + 1]);
        }

        /// <summary>Advance the cursor by <paramref name="count"/> bytes.</summary>
        public void Skip(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (_pos + count > _end) throw EndOfStream(count);
            _pos += count;
        }

        /// <summary>Reposition the cursor to <paramref name="position"/> relative to the codestream start.</summary>
        public void Seek(int position)
        {
            if (position < 0 || position > Length)
                throw new ArgumentOutOfRangeException(nameof(position));
            _pos = _start + position;
        }

        /// <summary>
        /// Read a 2-byte marker code, validating that the high byte is 0xFF and
        /// the resulting code is in the Part 1 marker range. Throws on garbage.
        /// </summary>
        public ushort ReadMarker()
        {
            int markerPos = Position;
            ushort code = ReadUInt16BigEndian();
            if (!MarkerCode.IsValidMarker(code))
            {
                throw new InvalidDataException(
                    $"Expected J2K marker at codestream position {markerPos}; got 0x{code:X4}.");
            }
            return code;
        }

        /// <summary>
        /// Read the 2-byte Lxxx segment-length field that follows a marker code,
        /// then return a sub-extent <see cref="CodestreamReader"/> covering just
        /// the payload (Lxxx - 2 bytes). The parent cursor advances past the
        /// entire segment. Returns the sub-reader positioned at the start of
        /// the payload.
        /// </summary>
        public CodestreamReader ReadSegment()
        {
            ushort lxxx = ReadUInt16BigEndian();
            if (lxxx < 2)
                throw new InvalidDataException($"Marker segment length {lxxx} < 2 at codestream position {Position - 2}.");
            int payloadLen = lxxx - 2;
            if (_pos + payloadLen > _end) throw EndOfStream(payloadLen);
            var sub = new CodestreamReader(_buffer, _pos, payloadLen);
            _pos += payloadLen;
            return sub;
        }

        private EndOfStreamException EndOfStream(int wanted)
        {
            return new EndOfStreamException(
                $"Tried to read {wanted} byte(s) at codestream position {Position}, only {Remaining} byte(s) remain.");
        }
    }
}
