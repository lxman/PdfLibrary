using FontParser.Reader;

namespace FontParser.Tables.Colr.PaintTables
{
    public class PaintVarScaleAroundCenter : IPaintTable
    {
        public byte Format => 19;

        public IPaintTable SubTable { get; }

        public float ScaleX { get; }

        public float ScaleY { get; }

        public short CenterX { get; }

        public short CenterY { get; }

        public uint VarIndexBase { get; }

        public PaintVarScaleAroundCenter(BigEndianReader reader)
        {
            long start = reader.Position - 1;
            uint paintOffset = reader.ReadUInt24();
            ScaleX = reader.ReadF2Dot14();
            ScaleY = reader.ReadF2Dot14();
            CenterX = reader.ReadShort();
            CenterY = reader.ReadShort();
            VarIndexBase = reader.ReadUInt32();
            SubTable = PaintTableFactory.CreatePaintTable(reader, start + paintOffset);
        }
    }
}