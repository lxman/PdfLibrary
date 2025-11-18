namespace PdfLibrary.Fonts.Embedded.Tables.Cmap.SubTables
{
    /// <summary>
    /// Non-default UVS table header for cmap format 14
    /// Contains non-default Unicode variation sequences
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
    public class NonDefaultUvsTableHeader
    {
        public List<UvsMappingRecord> UvsMappings { get; } = new List<UvsMappingRecord>();

        public NonDefaultUvsTableHeader(BigEndianReader reader)
        {
            uint numUvsMappings = reader.ReadUInt32();
            for (var i = 0; i < numUvsMappings; i++)
            {
                UvsMappings.Add(new UvsMappingRecord(reader.ReadBytes(UvsMappingRecord.RecordSize)));
            }
        }
    }
}
