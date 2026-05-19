using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Bloc.BitmapIndexSubtable
{
    public class BitmapIndexSubtableFormat1 : IBitmapIndexSubtable
    {
        public IndexFormat IndexFormat { get; }

        public BlocImageFormat ImageFormat { get; }

        public BitmapIndexSubtableFormat1(BigEndianReader reader)
        {
            IndexFormat = (IndexFormat)reader.ReadUShort();
            ImageFormat = (BlocImageFormat)reader.ReadUShort();
            uint imageDataOffset = reader.ReadUInt32();
            //List<uint> offsets =
        }
    }
}