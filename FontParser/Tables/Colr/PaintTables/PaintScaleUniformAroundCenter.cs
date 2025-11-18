using FontParser.Reader;

namespace FontParser.Tables.Colr.PaintTables
{
    public class PaintScaleUniformAroundCenter : IPaintTable
    {
        public byte Format => 22;

        public IPaintTable SubTable { get; }

        public float Scale { get; }

        public short CenterX { get; }

        public short CenterY { get; }

        public PaintScaleUniformAroundCenter(BigEndianReader reader)
        {
            long start = reader.Position - 1;
            uint paintOffset = reader.ReadUInt24();
            Scale = reader.ReadF2Dot14();
            CenterX = reader.ReadShort();
            CenterY = reader.ReadShort();
            SubTable = PaintTableFactory.CreatePaintTable(reader, start + paintOffset);
        }
    }
}