using FontParser.Reader;
using FontParser.Tables.Bitmap.Common;

namespace FontParser.Tables.Proprietary.Aat.Bloc
{
    public class BitmapSizeTable
    {
        public uint IndexSubTableArrayOffset { get; }

        public uint IndexTablesSize { get; }

        public uint NumberOfIndexSubTables { get; }

        public uint ColorRef { get; }

        public SbitLineMetrics HorizLineMetrics { get; }

        public SbitLineMetrics VertLineMetrics { get; }

        public ushort StartGlyphIndex { get; }

        public ushort EndGlyphIndex { get; }

        public byte PpemX { get; }

        public byte PpemY { get; }

        public BitmapDepth BitDepth { get; }

        public BitmapSizeFlag Flags { get; }

        public BitmapSizeTable(BigEndianReader reader)
        {
            IndexSubTableArrayOffset = reader.ReadUInt32();
            IndexTablesSize = reader.ReadUInt32();
            NumberOfIndexSubTables = reader.ReadUInt32();
            ColorRef = reader.ReadUInt32();
            HorizLineMetrics = new SbitLineMetrics(reader);
            VertLineMetrics = new SbitLineMetrics(reader);
            StartGlyphIndex = reader.ReadUShort();
            EndGlyphIndex = reader.ReadUShort();
            PpemX = reader.ReadByte();
            PpemY = reader.ReadByte();
            BitDepth = (BitmapDepth)reader.ReadByte();
            Flags = (BitmapSizeFlag)reader.ReadByte();
        }
    }
}