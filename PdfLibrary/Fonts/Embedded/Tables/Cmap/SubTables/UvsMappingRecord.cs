namespace PdfLibrary.Fonts.Embedded.Tables.Cmap.SubTables
{
    /// <summary>
    /// UVS mapping record for cmap format 14 (variation sequences)
    /// Maps Unicode values to glyph IDs for non-default variations
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
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
