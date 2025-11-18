using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Bitmap.Common.IndexSubtables
{
    public class IndexSubtableFormat1 : IIndexSubtable
    {
        public ushort IndexFormat { get; }

        public ushort ImageFormat { get; }

        public uint ImageDataOffset { get; }

        public List<uint> BitmapDataOffsets { get; } = new List<uint>();

        public IndexSubtableFormat1(BigEndianReader reader, ushort numOffsets)
        {
            IndexFormat = reader.ReadUShort();
            ImageFormat = reader.ReadUShort();
            ImageDataOffset = reader.ReadUInt32();
            for (var i = 0; i < numOffsets; i++)
            {
                BitmapDataOffsets.Add(reader.ReadUInt32());
            }
        }
    }
}