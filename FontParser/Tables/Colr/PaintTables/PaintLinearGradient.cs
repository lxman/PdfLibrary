using FontParser.Reader;

namespace FontParser.Tables.Colr.PaintTables
{
    public class PaintLinearGradient : IPaintTable
    {
        public byte Format => 4;

        public ColorLine ColorLine { get; }

        public short X0 { get; }

        public short Y0 { get; }

        public short X1 { get; }

        public short Y1 { get; }

        public short X2 { get; }

        public short Y2 { get; }

        public PaintLinearGradient(BigEndianReader reader)
        {
            long start = reader.Position - 1;

            uint colorLineOffset = reader.ReadUInt24();
            X0 = reader.ReadShort();
            Y0 = reader.ReadShort();
            X1 = reader.ReadShort();
            Y1 = reader.ReadShort();
            X2 = reader.ReadShort();
            Y2 = reader.ReadShort();
            reader.Seek(start + colorLineOffset);
            ColorLine = new ColorLine(reader);
        }
    }
}