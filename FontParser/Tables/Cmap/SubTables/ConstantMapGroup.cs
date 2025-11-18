using FontParser.Reader;

namespace FontParser.Tables.Cmap.SubTables
{
    public class ConstantMapGroup
    {
        public static long RecordSize => 12;

        public uint StartCharCode { get; }

        public uint EndCharCode { get; }

        public uint GlyphId { get; }

        public ConstantMapGroup(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            StartCharCode = reader.ReadUInt32();
            EndCharCode = reader.ReadUInt32();
            GlyphId = reader.ReadUInt32();
        }
    }
}