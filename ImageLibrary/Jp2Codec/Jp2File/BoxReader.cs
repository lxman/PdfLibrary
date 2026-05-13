using System;
using System.IO;

namespace Jp2Codec.Jp2File
{
    /// <summary>
    /// Header of a JP2 box (ISO/IEC 15444-1 I.3.1). Holds the type, the byte
    /// range covered by the box's contents (excluding the LBox/TBox/XLBox
    /// fields), and whether the box used the extended 16-byte header form.
    /// </summary>
    internal readonly struct BoxHeader
    {
        /// <summary>Four-character box type (TBox) packed as a big-endian uint32.</summary>
        public uint Type { get; }

        /// <summary>Absolute offset (in the host buffer) of this box's first byte (start of LBox).</summary>
        public int BoxStart { get; }

        /// <summary>Absolute offset (in the host buffer) of the contents (just past TBox/XLBox).</summary>
        public int ContentStart { get; }

        /// <summary>
        /// Length of the contents in bytes. Excludes the LBox/TBox/XLBox header.
        /// Set to -1 to indicate "extends to EOF" (LBox = 0).
        /// </summary>
        public long ContentLength { get; }

        public BoxHeader(uint type, int boxStart, int contentStart, long contentLength)
        {
            Type = type;
            BoxStart = boxStart;
            ContentStart = contentStart;
            ContentLength = contentLength;
        }
    }

    /// <summary>
    /// Iterator over JP2 boxes within a byte buffer. Each <see cref="ReadNext"/>
    /// call returns the next top-level box header and advances past its
    /// contents. Boxes nested inside a superbox (e.g. jp2h) are walked by
    /// constructing a child <see cref="BoxReader"/> over the parent's content
    /// range.
    /// </summary>
    internal sealed class BoxReader
    {
        private readonly byte[] _buffer;
        private readonly int _start;
        private readonly int _end;
        private int _pos;

        public BoxReader(byte[] buffer) : this(buffer, 0, buffer?.Length ?? 0) { }

        public BoxReader(byte[] buffer, int offset, int length)
        {
            if (buffer is null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if (length < 0 || offset + length > buffer.Length) throw new ArgumentOutOfRangeException(nameof(length));
            _buffer = buffer;
            _start = offset;
            _end = offset + length;
            _pos = offset;
        }

        public bool IsAtEnd => _pos >= _end;
        public int Remaining => _end - _pos;
        public byte[] Buffer => _buffer;

        /// <summary>
        /// Read the next box header. Returns true and fills <paramref name="header"/>
        /// on success; returns false if positioned at end. The cursor advances
        /// past the box contents so subsequent calls walk siblings.
        /// </summary>
        public bool ReadNext(out BoxHeader header)
        {
            if (_pos >= _end)
            {
                header = default;
                return false;
            }
            if (_pos + 8 > _end)
                throw new InvalidDataException(
                    $"JP2 box header truncated at offset {_pos - _start} (need 8 bytes, only {_end - _pos} remain).");

            int boxStart = _pos;
            uint lbox = ReadUInt32BigEndian(_pos);
            uint tbox = ReadUInt32BigEndian(_pos + 4);
            int contentStart;
            long contentLength;

            if (lbox == 1)
            {
                if (_pos + 16 > _end)
                    throw new InvalidDataException(
                        $"JP2 box {BoxType.Format(tbox)} declares extended length but XLBox bytes are truncated.");
                ulong xl = ((ulong)ReadUInt32BigEndian(_pos + 8) << 32) | ReadUInt32BigEndian(_pos + 12);
                if (xl < 16) throw new InvalidDataException($"JP2 box {BoxType.Format(tbox)}: XLBox {xl} < 16.");
                contentStart = _pos + 16;
                contentLength = (long)xl - 16;
            }
            else if (lbox == 0)
            {
                // "Extends to EOF" — content is everything from after TBox to _end.
                contentStart = _pos + 8;
                contentLength = _end - contentStart;
            }
            else
            {
                if (lbox < 8) throw new InvalidDataException($"JP2 box {BoxType.Format(tbox)}: LBox {lbox} < 8.");
                contentStart = _pos + 8;
                contentLength = (long)lbox - 8;
            }

            if (contentStart + contentLength > _end)
                throw new InvalidDataException(
                    $"JP2 box {BoxType.Format(tbox)} overruns buffer: content end @ {contentStart + contentLength}, buffer end @ {_end}.");

            header = new BoxHeader(tbox, boxStart, contentStart, contentLength);
            _pos = (int)(contentStart + contentLength);
            return true;
        }

        /// <summary>Return a child reader scoped to the contents of <paramref name="header"/>.</summary>
        public BoxReader OpenChild(BoxHeader header)
        {
            if (header.ContentLength < 0)
                throw new InvalidDataException("Cannot open child reader on an open-ended (LBox=0) box.");
            return new BoxReader(_buffer, header.ContentStart, (int)header.ContentLength);
        }

        private uint ReadUInt32BigEndian(int at)
        {
            return ((uint)_buffer[at] << 24)
                 | ((uint)_buffer[at + 1] << 16)
                 | ((uint)_buffer[at + 2] << 8)
                 | _buffer[at + 3];
        }
    }
}
