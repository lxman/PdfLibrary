using FontParser.Reader;

namespace FontParser.Tables.Colr.PaintTables
{
    public class PaintGlyph : IPaintTable
    {
        public byte Format => 10;

        public uint PaintOffset { get; }

        public ushort GlyphId { get; }

        public IPaintTable PaintTable { get; }

        public PaintGlyph(BigEndianReader reader)
        {
            long start = reader.Position - 1;
            PaintOffset = reader.ReadUInt24();
            GlyphId = reader.ReadUShort();
            PaintTable = PaintTableFactory.CreatePaintTable(reader, start + PaintOffset);
        }
    }
}