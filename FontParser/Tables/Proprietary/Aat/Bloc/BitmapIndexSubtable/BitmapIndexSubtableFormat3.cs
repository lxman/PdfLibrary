using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Bloc.BitmapIndexSubtable
{
    public class BitmapIndexSubtableFormat3 : IBitmapIndexSubtable
    {
        public IndexFormat IndexFormat { get; }

        public BlocImageFormat ImageFormat { get; }

        public List<ushort> OffsetArray { get; } = new List<ushort>();

        public BitmapIndexSubtableFormat3(BigEndianReader reader)
        {
            IndexFormat = (IndexFormat)reader.ReadUShort();
            ImageFormat = (BlocImageFormat)reader.ReadUShort();
            ushort offsetArrayCount = reader.ReadUShort();
            for (var i = 0; i < offsetArrayCount; i++)
            {
                OffsetArray.Add(reader.ReadUShort());
            }
        }
    }
}