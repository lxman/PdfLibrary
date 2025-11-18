using System;
using System.Buffers.Binary;
using FontParser.Reader;
using FontParser.Tables.Bitmap.Common.IndexSubtables;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace FontParser.Tables.Bitmap.Common
{
    public class IndexSubtableRecord
    {
        public ushort FirstGlyphIndex { get; }

        public ushort LastGlyphIndex { get; }

        public IIndexSubtable Subtable { get; private set; }

        private readonly long _readerStart;

        public IndexSubtableRecord(BigEndianReader reader, long start)
        {
            FirstGlyphIndex = reader.ReadUShort();
            LastGlyphIndex = reader.ReadUShort();
            uint offset = reader.ReadUInt32();
            _readerStart = start + offset;
        }

        public void ReadSubtable(BigEndianReader reader)
        {
            reader.Seek(_readerStart);
            ushort format = BinaryPrimitives.ReadUInt16BigEndian(reader.PeekBytes(2));
            var numOffsets = Convert.ToUInt16(LastGlyphIndex - FirstGlyphIndex + 2);
            Subtable = format switch
            {
                1 => new IndexSubtableFormat1(reader, numOffsets),
                2 => new IndexSubtablesFormat2(reader),
                3 => new IndexSubtablesFormat3(reader, numOffsets),
                4 => new IndexSubtablesFormat4(reader),
                5 => new IndexSubtablesFormat5(reader),
                _ => Subtable
            };
        }
    }
}