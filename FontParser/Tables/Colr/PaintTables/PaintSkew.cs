using FontParser.Reader;

namespace FontParser.Tables.Colr.PaintTables
{
    public class PaintSkew : IPaintTable
    {
        public byte Format => 28;

        public IPaintTable SubTable { get; }

        public float XSkewAngle { get; }

        public float YSkewAngle { get; }

        public PaintSkew(BigEndianReader reader)
        {
            long start = reader.Position - 1;
            uint subTableOffset = reader.ReadUInt24();

            XSkewAngle = reader.ReadF2Dot14();
            YSkewAngle = reader.ReadF2Dot14();

            SubTable = PaintTableFactory.CreatePaintTable(reader, start + subTableOffset);
        }
    }
}