using FontParser.Reader;

namespace FontParser.Tables.Colr
{
    public class BaseGlyphPaintRecord
    {
        public ushort GlyphId { get; }

        public IPaintTable SubTable { get; }

        public BaseGlyphPaintRecord(BigEndianReader reader, long start)
        {
            GlyphId = reader.ReadUShort();
            uint offset = reader.ReadUInt32();
            long position = reader.Position;
            SubTable = PaintTableFactory.CreatePaintTable(reader, start + offset);
            reader.Seek(position);
        }
    }
}