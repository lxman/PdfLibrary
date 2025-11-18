namespace PdfLibrary.Fonts.Embedded.Tables.Cmap.SubTables
{
    /// <summary>
    /// Cmap subtable format 10 - Trimmed array (32-bit)
    /// Maps a contiguous range of 32-bit codes to glyph IDs
    /// Used for full Unicode support with trimmed ranges
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
    public class CmapSubtablesFormat10 : ICmapSubtable
    {
        public int Language { get; }

        public uint StartChar { get; }

        public List<uint> GlyphIndexArray { get; } = new List<uint>();

        private readonly uint _numChars;

        public CmapSubtablesFormat10(BigEndianReader reader)
        {
            ushort format = reader.ReadUShort();
            _ = reader.ReadUShort();
            uint length = reader.ReadUInt32();
            Language = reader.ReadInt32();
            StartChar = reader.ReadUInt32();
            _numChars = reader.ReadUInt32();
            for (var i = 0; i < _numChars; i++)
            {
                GlyphIndexArray.Add(reader.ReadUShort());
            }
        }

        public ushort GetGlyphId(ushort codePoint)
        {
            uint index = codePoint - StartChar;
            if (index < _numChars)
            {
                return (ushort)GlyphIndexArray[(int)index];
            }
            return 0;
        }
    }
}
