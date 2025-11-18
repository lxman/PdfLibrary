using FontParser.Reader;

namespace FontParser.Tables.Cmap.SubTables
{
    public class UnicodeRangeRecord
    {
        public static long RecordSize => 4;

        public uint StartUnicodeValue { get; set; }

        public byte AdditionalCount { get; set; }

        public UnicodeRangeRecord(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            StartUnicodeValue = reader.ReadUInt24();
            AdditionalCount = reader.ReadByte();
        }
    }
}