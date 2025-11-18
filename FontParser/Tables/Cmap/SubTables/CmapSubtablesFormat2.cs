using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Cmap.SubTables
{
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
            for (var i = 0; i < 256; i++)
            {
                SubHeaderKeys.Add((byte)(reader.ReadUShort() >> 8));
            }
            foreach (ushort key in SubHeaderKeys)
            {
                reader.Seek(key);
                SubHeaders.Add(new Format2SubHeader(reader.ReadBytes(Format2SubHeader.RecordSize)));
            }
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