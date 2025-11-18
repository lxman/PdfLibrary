using FontParser.Reader;

namespace FontParser.Tables.Colr.PaintTables
{
    public class PaintVarSweepGradient : IPaintTable
    {
        public byte Format => 9;

        public VarColorLine ColorLine { get; }

        public short CenterX { get; }

        public short CenterY { get; }

        public float StartAngle { get; }

        public float EndAngle { get; }

        public uint VarIndexBase { get; }

        public PaintVarSweepGradient(BigEndianReader reader)
        {
            long start = reader.Position - 1;
            uint colorLineOffset = reader.ReadUInt24();
            CenterX = reader.ReadShort();
            CenterY = reader.ReadShort();
            StartAngle = reader.ReadF2Dot14();
            EndAngle = reader.ReadF2Dot14();
            VarIndexBase = reader.ReadUInt32();
            reader.Seek(start + colorLineOffset);
            ColorLine = new VarColorLine(reader);
        }
    }
}