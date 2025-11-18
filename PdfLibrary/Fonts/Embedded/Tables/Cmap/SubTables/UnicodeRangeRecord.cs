namespace PdfLibrary.Fonts.Embedded.Tables.Cmap.SubTables
{
    /// <summary>
    /// Unicode range record for cmap format 14 (variation sequences)
    /// Defines default UVS ranges
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
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
