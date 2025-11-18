using FontParser.Reader;

namespace FontParser.Tables.Colr.PaintTables
{
    public class PaintRadialGradient : IPaintTable
    {
        public byte Format => 6;

        public ColorLine ColorLine { get; }

        public short X0 { get; }

        public short Y0 { get; }

        public ushort Radius0 { get; }

        public short X1 { get; }

        public short Y1 { get; }

        public ushort Radius1 { get; }

        public PaintRadialGradient(BigEndianReader reader)
        {
            long start = reader.Position - 1;
            uint colorLineOffset = reader.ReadUInt24();
            X0 = reader.ReadShort();
            Y0 = reader.ReadShort();
            Radius0 = reader.ReadUShort();
            X1 = reader.ReadShort();
            Y1 = reader.ReadShort();
            Radius1 = reader.ReadUShort();
            long position = reader.Position;
            reader.Seek(start + colorLineOffset);
            ColorLine = new ColorLine(reader);
            reader.Seek(position);
        }
    }
}