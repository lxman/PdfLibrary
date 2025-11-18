using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Morx.LookupTables
{
    public class LookupSingle
    {
        public ushort GlyphIndex { get; }

        public byte[] Value { get; }

        public LookupSingle(BigEndianReader reader, ushort width)
        {
            GlyphIndex = reader.ReadUShort();
            Value = reader.ReadBytes(width - 2);
        }
    }
}