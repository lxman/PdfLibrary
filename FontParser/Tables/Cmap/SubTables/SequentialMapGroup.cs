using FontParser.Reader;

namespace FontParser.Tables.Cmap.SubTables
{
    public class SequentialMapGroup
    {
        public static long RecordSize => 12;

        public uint StartCharCode { get; }

        public uint EndCharCode { get; }

        public uint StartGlyphId { get; }

        public SequentialMapGroup(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            StartCharCode = reader.ReadUInt32();
            EndCharCode = reader.ReadUInt32();
            StartGlyphId = reader.ReadUInt32();
        }

        public override string ToString()
        {
            return $"StartCharCode: {StartCharCode}, EndCharCode: {EndCharCode}, StartGlyphId: {StartGlyphId}";
        }
    }
}