using FontParser.Reader;

namespace FontParser.Tables.Colr
{
    public class ClipBox
    {
        public byte Format { get; }

        public short XMin { get; }

        public short YMin { get; }

        public short XMax { get; }

        public short YMax { get; }

        public uint? VarIndexBase { get; }

        public ClipBox(BigEndianReader reader)
        {
            Format = reader.ReadByte();
            XMin = reader.ReadShort();
            YMin = reader.ReadShort();
            XMax = reader.ReadShort();
            YMax = reader.ReadShort();
            if (Format == 2)
            {
                VarIndexBase = reader.ReadUInt32();
            }
        }
    }
}