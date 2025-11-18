using FontParser.Reader;

namespace FontParser.Tables.Colr.PaintTables
{
    public class PaintRotateAroundCenter : IPaintTable
    {
        public byte Format => 26;

        public IPaintTable SubTable { get; }

        public float Angle { get; }

        public short CenterX { get; }

        public short CenterY { get; }

        public PaintRotateAroundCenter(BigEndianReader reader)
        {
            long start = reader.Position - 1;
            uint subTableOffset = reader.ReadUInt24();

            Angle = reader.ReadF2Dot14();
            CenterX = reader.ReadShort();
            CenterY = reader.ReadShort();
            SubTable = PaintTableFactory.CreatePaintTable(reader, start + subTableOffset);
        }
    }
}