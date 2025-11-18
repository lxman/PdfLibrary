using FontParser.Reader;

namespace FontParser.Tables.Colr.PaintTables
{
    public class PaintTranslate : IPaintTable
    {
        public byte Format => 14;

        public IPaintTable SubTable { get; }

        public short Dx { get; }

        public short Dy { get; }

        public PaintTranslate(BigEndianReader reader)
        {
            long start = reader.Position - 1;
            uint paintOffset = reader.ReadUInt24();
            Dx = reader.ReadShort();
            Dy = reader.ReadShort();
            SubTable = PaintTableFactory.CreatePaintTable(reader, start + paintOffset);
        }
    }
}