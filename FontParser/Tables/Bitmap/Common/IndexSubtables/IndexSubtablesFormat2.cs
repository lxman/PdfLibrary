using FontParser.Reader;

namespace FontParser.Tables.Bitmap.Common.IndexSubtables
{
    public class IndexSubtablesFormat2 : IIndexSubtable
    {
        public ushort IndexFormat { get; }

        public ushort ImageFormat { get; }

        public uint ImageDataOffset { get; }

        public uint ImageSize { get; }

        public BigGlyphMetricsRecord BigMetrics { get; }

        public IndexSubtablesFormat2(BigEndianReader reader)
        {
            IndexFormat = reader.ReadUShort();
            ImageFormat = reader.ReadUShort();
            ImageDataOffset = reader.ReadUInt32();
            ImageSize = reader.ReadUInt32();
            BigMetrics = new BigGlyphMetricsRecord(reader);
        }
    }
}