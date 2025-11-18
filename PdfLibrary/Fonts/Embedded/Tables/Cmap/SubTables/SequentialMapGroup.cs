namespace PdfLibrary.Fonts.Embedded.Tables.Cmap.SubTables
{
    /// <summary>
    /// Sequential map group for cmap formats 12 and 13
    /// Maps a contiguous range of characters to glyphs
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
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
