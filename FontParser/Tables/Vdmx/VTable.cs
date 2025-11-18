using FontParser.Reader;

namespace FontParser.Tables.Vdmx
{
    public class VTable
    {
        public ushort YPelHeight { get; set; }

        public short YMax { get; set; }

        public short YMin { get; set; }

        public VTable(BigEndianReader reader)
        {
            YPelHeight = reader.ReadUShort();
            YMax = reader.ReadShort();
            YMin = reader.ReadShort();
        }
    }
}