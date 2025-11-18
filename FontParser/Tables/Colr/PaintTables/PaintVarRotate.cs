using FontParser.Reader;

namespace FontParser.Tables.Colr.PaintTables
{
    public class PaintVarRotate : IPaintTable
    {
        public byte Format => 25;

        public float Angle { get; }

        public uint VarIndexBase { get; }

        public IPaintTable SubTable { get; }

        public PaintVarRotate(BigEndianReader reader)
        {
            long start = reader.Position - 1;
            uint subTableOffset = reader.ReadUInt24();
            Angle = reader.ReadF2Dot14();
            VarIndexBase = reader.ReadUInt32();
            SubTable = PaintTableFactory.CreatePaintTable(reader, start + subTableOffset);
        }
    }
}