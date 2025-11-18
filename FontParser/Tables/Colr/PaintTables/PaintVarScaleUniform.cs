using FontParser.Reader;

namespace FontParser.Tables.Colr.PaintTables
{
    public class PaintVarScaleUniform : IPaintTable
    {
        public byte Format => 21;

        public IPaintTable SubTable { get; }

        public float Scale { get; }

        public uint VarIndexBase { get; }

        public PaintVarScaleUniform(BigEndianReader reader)
        {
            long start = reader.Position - 1;
            uint paintOffset = reader.ReadUInt24();
            Scale = reader.ReadF2Dot14();
            VarIndexBase = reader.ReadUInt32();
            SubTable = PaintTableFactory.CreatePaintTable(reader, start + paintOffset);
        }
    }
}