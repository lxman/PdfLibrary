using System.Collections.Generic;
using System.Linq;
using FontParser.Reader;

namespace FontParser.Tables.Bitmap.Common.IndexSubtables
{
    public class IndexSubtablesFormat3 : IIndexSubtable
    {
        public ushort IndexFormat { get; }

        public ushort ImageFormat { get; }

        public uint ImageDataOffset { get; }

        public List<ushort> BitmapDataOffsets { get; }

        public IndexSubtablesFormat3(BigEndianReader reader, ushort numOffsets)
        {
            IndexFormat = reader.ReadUShort();
            ImageFormat = reader.ReadUShort();
            ImageDataOffset = reader.ReadUInt32();
            BitmapDataOffsets = reader.ReadUShortArray(numOffsets).ToList();
        }
    }
}