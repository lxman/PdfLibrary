namespace PdfLibrary.Fonts.Embedded.Tables.Cmap.SubTables
{
    /// <summary>
    /// Default UVS table header for cmap format 14
    /// Contains default Unicode variation sequences
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
    public class DefaultUvsTableHeader
    {
        public List<UnicodeRangeRecord> UnicodeRangeRecords { get; } = new List<UnicodeRangeRecord>();

        public DefaultUvsTableHeader(BigEndianReader reader)
        {
            uint numUnicodeRangeRecords = reader.ReadUInt32();
            for (var i = 0; i < numUnicodeRangeRecords; i++)
            {
                UnicodeRangeRecords.Add(new UnicodeRangeRecord(reader.ReadBytes(UnicodeRangeRecord.RecordSize)));
            }
        }
    }
}
