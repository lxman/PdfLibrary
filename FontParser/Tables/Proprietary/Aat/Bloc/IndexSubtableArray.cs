using System.Buffers.Binary;
using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Proprietary.Aat.Bloc.BitmapIndexSubtable;

namespace FontParser.Tables.Proprietary.Aat.Bloc
{
    public class IndexSubtableArray
    {
        public ushort FirstGlyphIndex { get; }

        public ushort LastGlyphIndex { get; }

        public List<IBitmapIndexSubtable> BitmapIndexSubtables { get; } = new List<IBitmapIndexSubtable>();

        private readonly long _offset;
        private readonly BigEndianReader _reader;

        public IndexSubtableArray(BigEndianReader reader)
        {
            FirstGlyphIndex = reader.ReadUShort();
            LastGlyphIndex = reader.ReadUShort();
            _reader = reader;
            _offset = reader.ReadUInt32();
        }

        public void Process()
        {
            _reader.Seek(_offset);
            ushort format = BinaryPrimitives.ReadUInt16BigEndian(_reader.PeekBytes(2));
            switch (format)
            {
                case 1:
                    BitmapIndexSubtables.Add(new BitmapIndexSubtableFormat1(_reader));
                    break;

                case 2:
                    BitmapIndexSubtables.Add(new BitmapIndexSubtableFormat2(_reader));
                    break;

                case 3:
                    BitmapIndexSubtables.Add(new BitmapIndexSubtableFormat3(_reader));
                    break;
            }
        }
    }
}