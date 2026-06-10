using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Cmap.SubTables
{
    /// <summary>
    /// cmap format 2 — high-byte mapping for mixed 8/16-bit CJK encodings (Shift-JIS, Big5,
    /// etc.). subHeaderKeys[firstByte] gives subHeaderIndex*8; subHeader 0 handles single-byte
    /// codes, others handle the trailing byte of a two-byte code.
    /// </summary>
    public class CmapSubtablesFormat2 : ICmapSubtable
    {
        public int Language { get; }

        // Raw subHeaderKeys values (each = subHeaderIndex * 8), indexed by first byte.
        public List<ushort> SubHeaderKeys { get; } = new List<ushort>();

        public List<Format2SubHeader> SubHeaders { get; } = new List<Format2SubHeader>();

        public List<ushort> GlyphIndexArray { get; } = new List<ushort>();

        private readonly int _numSubHeaders;

        public CmapSubtablesFormat2(BigEndianReader reader)
        {
            _ = reader.ReadUShort();              // format (2)
            uint length = reader.ReadUShort();
            Language = reader.ReadInt16();

            var maxKey = 0;
            for (var i = 0; i < 256; i++)
            {
                ushort key = reader.ReadUShort(); // = subHeaderIndex * 8 (NOT a high byte)
                SubHeaderKeys.Add(key);
                if (key > maxKey) maxKey = key;
            }

            _numSubHeaders = maxKey / 8 + 1;
            for (var i = 0; i < _numSubHeaders; i++)
            {
                SubHeaders.Add(new Format2SubHeader(reader.ReadBytes(Format2SubHeader.RecordSize)));
            }

            // glyphIndexArray is whatever remains after header(6) + keys(512) + subHeaders.
            long remaining = (long)length - 6 - 512 - (long)_numSubHeaders * Format2SubHeader.RecordSize;
            for (var i = 0; i < remaining / 2 && reader.BytesRemaining >= 2; i++)
            {
                GlyphIndexArray.Add(reader.ReadUShort());
            }
        }

        public ushort GetGlyphId(ushort codePoint)
        {
            if (SubHeaderKeys.Count < 256 || SubHeaders.Count == 0) return 0;

            int high = codePoint >> 8;
            int low = codePoint & 0xFF;

            int subHeaderIndex;
            int entryByte; // the byte indexed within the chosen subHeader
            if (high == 0)
            {
                // Single byte: subHeader 0 maps it directly, unless `low` is actually a lead byte.
                if (SubHeaderKeys[low] / 8 != 0) return 0;
                subHeaderIndex = 0;
                entryByte = low;
            }
            else
            {
                subHeaderIndex = SubHeaderKeys[high] / 8;
                entryByte = low;
            }

            if (subHeaderIndex < 0 || subHeaderIndex >= SubHeaders.Count) return 0;
            Format2SubHeader subHeader = SubHeaders[subHeaderIndex];

            if (entryByte < subHeader.FirstCode || entryByte >= subHeader.FirstCode + subHeader.EntryCount)
            {
                return 0;
            }

            // idRangeOffset is relative to its own field position. With the arrays flattened in
            // order, the glyphIndexArray list index reduces to (derivation in the review notes):
            //   idRangeOffset/2 + (entryByte - firstCode) + 3 + 4*(subHeaderIndex - numSubHeaders)
            int index = subHeader.IdRangeOffset / 2
                        + (entryByte - subHeader.FirstCode)
                        + 3
                        + 4 * (subHeaderIndex - _numSubHeaders);

            if (index < 0 || index >= GlyphIndexArray.Count) return 0;

            ushort glyphIndex = GlyphIndexArray[index];
            if (glyphIndex == 0) return 0;
            return (ushort)((glyphIndex + subHeader.IdDelta) % 65536);
        }
    }
}
