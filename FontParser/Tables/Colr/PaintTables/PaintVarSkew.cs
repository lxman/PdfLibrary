using FontParser.Reader;

namespace FontParser.Tables.Colr.PaintTables
{
    public class PaintVarSkew : IPaintTable
    {
        public byte Format => 29;

        public IPaintTable SubTable { get; }

        public float XSkewAngle { get; }

        public float YSkewAngle { get; }

        public uint VarIndexBase { get; }

        public PaintVarSkew(BigEndianReader reader)
        {
            long start = reader.Position - 1;
            uint subTableOffset = reader.ReadUInt24();

            XSkewAngle = reader.ReadF2Dot14();
            YSkewAngle = reader.ReadF2Dot14();
            VarIndexBase = reader.ReadUInt32();

            SubTable = PaintTableFactory.CreatePaintTable(reader, start + subTableOffset);
        }
    }
}