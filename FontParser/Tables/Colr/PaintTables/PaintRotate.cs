using FontParser.Reader;

namespace FontParser.Tables.Colr.PaintTables
{
    public class PaintRotate : IPaintTable
    {
        public byte Format => 24;

        public IPaintTable SubTable { get; }

        public float Angle { get; }

        public PaintRotate(BigEndianReader reader)
        {
            long start = reader.Position - 1;
            uint subTableOffset = reader.ReadUInt24();

            Angle = reader.ReadF2Dot14();

            SubTable = PaintTableFactory.CreatePaintTable(reader, start + subTableOffset);
        }
    }
}