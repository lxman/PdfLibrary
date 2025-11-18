using FontParser.Reader;

namespace FontParser.Tables.Colr.PaintTables
{
    public class PaintSkewAroundCenter : IPaintTable
    {
        public byte Format => 30;

        public IPaintTable SubTable { get; }

        public float XSkewAngle { get; }

        public float YSkewAngle { get; }

        public short CenterX { get; }

        public short CenterY { get; }

        public PaintSkewAroundCenter(BigEndianReader reader)
        {
            long start = reader.Position - 1;
            uint subTableOffset = reader.ReadUInt24();
            XSkewAngle = reader.ReadF2Dot14();
            YSkewAngle = reader.ReadF2Dot14();
            CenterX = reader.ReadShort();
            CenterY = reader.ReadShort();
            SubTable = PaintTableFactory.CreatePaintTable(reader, start + subTableOffset);
        }
    }
}