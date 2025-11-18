using FontParser.Reader;

namespace FontParser.Tables.Colr.PaintTables
{
    public class PaintVarRotateAroundCenter : IPaintTable
    {
        public byte Format => 27;

        public IPaintTable SubTable { get; }

        public float Angle { get; }

        public short CenterX { get; }

        public short CenterY { get; }

        public uint VarIndexBase { get; }

        public PaintVarRotateAroundCenter(BigEndianReader reader)
        {
            long start = reader.Position - 1;
            uint subTableOffset = reader.ReadUInt24();

            Angle = reader.ReadF2Dot14();
            CenterX = reader.ReadShort();
            CenterY = reader.ReadShort();
            VarIndexBase = reader.ReadUInt32();

            SubTable = PaintTableFactory.CreatePaintTable(reader, start + subTableOffset);
        }
    }
}