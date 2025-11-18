namespace PdfLibrary.Fonts.Embedded.Tables.Cmap.SubTables
{
    /// <summary>
    /// Cmap subtable format 6 - Trimmed table mapping
    /// Maps a contiguous range of codes to glyph IDs
    /// Common for optimized Unicode subsets
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
    public class CmapSubtablesFormat6 : ICmapSubtable
    {
        public int Language { get; }

        public uint FirstCode { get; }

        private readonly uint _entryCount;

        public List<uint> GlyphIndexArray { get; }

        public CmapSubtablesFormat6(BigEndianReader reader)
        {
            ushort format = reader.ReadUShort();
            uint length = reader.ReadUShort();
            Language = reader.ReadInt16();
            FirstCode = reader.ReadUShort();
            _entryCount = reader.ReadUShort();
            GlyphIndexArray = new List<uint>();
            for (var i = 0; i < _entryCount; i++)
            {
                GlyphIndexArray.Add(reader.ReadUShort());
            }
        }

        public ushort GetGlyphId(ushort codePoint)
        {
            if (codePoint < FirstCode || codePoint >= FirstCode + _entryCount)
            {
                return 0; // Code point is out of range
            }

            var index = (int)(codePoint - FirstCode);
            return (ushort)GlyphIndexArray[index];
        }
    }
}
