namespace PdfLibrary.Fonts.Embedded.Tables.Cmap.SubTables
{
    /// <summary>
    /// Constant map group for cmap format 13
    /// Maps a contiguous range of characters to the same glyph (many-to-one)
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
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
