namespace PdfLibrary.Fonts.Embedded.Tables
{
    /// <summary>
    /// Cmap subtable format 0 - Byte encoding table
    /// Simple 1-to-1 mapping from character codes 0-255 to glyph IDs
    /// Common in Macintosh Roman fonts and small font subsets
    /// </summary>
    public class CmapSubtablesFormat0 : ICmapSubtable
    {
        public int Language { get; }

        private readonly byte[] _glyphIdArray;

        public CmapSubtablesFormat0(BigEndianReader reader)
        {
            ushort format = reader.ReadUShort(); // Should be 0
            ushort length = reader.ReadUShort(); // Should be 262
            Language = reader.ReadUShort();

            // Read 256 bytes of glyph IDs
            _glyphIdArray = reader.ReadBytes(256);
        }

        public ushort GetGlyphId(ushort codePoint)
        {
            // Format 0 only supports character codes 0-255
            if (codePoint > 255)
                return 0;

            return _glyphIdArray[codePoint];
        }
    }
}
