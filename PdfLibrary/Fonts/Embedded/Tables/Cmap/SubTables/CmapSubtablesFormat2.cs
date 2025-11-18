namespace PdfLibrary.Fonts.Embedded.Tables.Cmap.SubTables
{
    /// <summary>
    /// Cmap subtable format 2 - High-byte mapping through table
    /// Mixed 8/16-bit encoding for legacy Asian fonts (CJK)
    /// Supports up to 256 subheaders for different byte ranges
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
    public class CmapSubtablesFormat2 : ICmapSubtable
    {
        public int Language { get; }

        public List<ushort> SubHeaderKeys { get; } = new List<ushort>();

        public List<Format2SubHeader> SubHeaders { get; } = new List<Format2SubHeader>();

        public List<ushort> GlyphIndexArray { get; } = new List<ushort>();

        public CmapSubtablesFormat2(BigEndianReader reader)
        {
            ushort format = reader.ReadUShort();
            uint length = reader.ReadUShort();
            _ = reader.ReadUShort();
            Language = reader.ReadInt16();

            // Read 256 subheader keys (byte offsets from start of subHeaderKeys array)
            for (var i = 0; i < 256; i++)
            {
                SubHeaderKeys.Add(reader.ReadUShort());
            }

            // Position after header (8 bytes) + subHeaderKeys (512 bytes) = 520
            // This is where subheaders start

            // Find unique subheader offsets and read each subheader only once
            var uniqueOffsets = SubHeaderKeys.Distinct().OrderBy(k => k).ToList();
            var subHeaderMap = new Dictionary<ushort, Format2SubHeader>();

            foreach (ushort offset in uniqueOffsets)
            {
                // Calculate absolute position: start of subHeaderKeys (position 8) + offset
                int absolutePos = 8 + offset;
                reader.Seek(absolutePos);
                var subHeader = new Format2SubHeader(reader.ReadBytes(Format2SubHeader.RecordSize));
                subHeaderMap[offset] = subHeader;
                SubHeaders.Add(subHeader);
            }

            // Read glyph index array
            // The reader should now be positioned right after the last subheader
            foreach (Format2SubHeader subHeader in SubHeaders)
            {
                for (var i = 0; i < subHeader.EntryCount; i++)
                {
                    GlyphIndexArray.Add(reader.ReadUShort());
                }
            }
        }

        public ushort GetGlyphId(ushort codePoint)
        {
            // Determine the high byte of the codePoint
            var highByte = (byte)(codePoint >> 8);

            // Get the subheader key for the high byte
            ushort subHeaderKey = SubHeaderKeys[highByte];

            // Get the corresponding subheader
            Format2SubHeader subHeader = SubHeaders[subHeaderKey];

            // Determine the low byte of the codePoint
            var lowByte = (byte)(codePoint & 0xFF);

            // Calculate the index in the GlyphIndexArray
            int index = subHeader.IdRangeOffset / 2 + lowByte - subHeader.FirstCode;

            // Check if the index is within the bounds of the GlyphIndexArray
            if (index < 0 || index >= GlyphIndexArray.Count)
            {
                return 0;
            }

            // Get the glyph index
            ushort glyphIndex = GlyphIndexArray[index];

            // If the glyph index is not 0, apply the idDelta
            if (glyphIndex != 0)
            {
                glyphIndex = (ushort)((glyphIndex + subHeader.IdDelta) % 65536);
            }

            return glyphIndex;
        }
    }
}
