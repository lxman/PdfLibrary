using FontParser.Reader;

namespace FontParser.Tables.Cmap.SubTables
{
    public class UvsMappingRecord
    {
        public static long RecordSize => 5;

        public uint UnicodeValue { get; }

        public ushort GlyphId { get; }

        public UvsMappingRecord(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            UnicodeValue = reader.ReadUInt24();
            GlyphId = reader.ReadUShort();
        }

        public override string ToString()
        {
            return $"Unicode Value: {UnicodeValue}, Glyph ID: {GlyphId}";
        }
    }
}