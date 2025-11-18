namespace PdfLibrary.Fonts.Embedded.Tables.Cmap.SubTables
{
    /// <summary>
    /// SubHeader structure for cmap format 2
    /// Used for mixed 8/16-bit encoding (legacy Asian fonts)
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
    public class Format2SubHeader
    {
        public static long RecordSize => 8;

        public ushort FirstCode { get; }

        public ushort EntryCount { get; }

        public short IdDelta { get; }

        public ushort IdRangeOffset { get; }

        public Format2SubHeader(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            FirstCode = reader.ReadUShort();
            EntryCount = reader.ReadUShort();
            IdDelta = reader.ReadShort();
            IdRangeOffset = reader.ReadUShort();
        }
    }
}
