using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Cmap.SubTables
{
    public class CmapSubtablesFormat4 : ICmapSubtable
    {
        public int Language { get; }

        public uint SegCountX2 { get; }

        public uint SearchRange { get; }

        public uint EntrySelector { get; }

        public uint RangeShift { get; }

        public List<ushort> EndCodes { get; } = new List<ushort>();

        public ushort ReservedPad { get; }

        public List<ushort> StartCodes { get; } = new List<ushort>();

        public List<short> IdDeltas { get; } = new List<short>();

        public List<ushort> IdRangeOffsets { get; } = new List<ushort>();

        public List<ushort> GlyphIdArray { get; } = new List<ushort>();

        public CmapSubtablesFormat4(BigEndianReader reader)
        {
            ushort format = reader.ReadUShort();
            uint length = reader.ReadUShort();
            Language = reader.ReadInt16();
            SegCountX2 = reader.ReadUShort();
            SearchRange = reader.ReadUShort();
            EntrySelector = reader.ReadUShort();
            RangeShift = reader.ReadUShort();

            for (var i = 0; i < SegCountX2 / 2; i++)
            {
                EndCodes.Add(reader.ReadUShort());
            }

            ReservedPad = reader.ReadUShort();

            for (var i = 0; i < SegCountX2 / 2; i++)
            {
                StartCodes.Add(reader.ReadUShort());
            }

            for (var i = 0; i < SegCountX2 / 2; i++)
            {
                IdDeltas.Add(reader.ReadShort());
            }

            for (var i = 0; i < SegCountX2 / 2; i++)
            {
                IdRangeOffsets.Add(reader.ReadUShort());
            }

            uint remainingBytes = length - 16 - (SegCountX2 * 4);
            for (var i = 0; i < remainingBytes / 2; i++)
            {
                GlyphIdArray.Add(reader.ReadUShort());
            }
        }

        public ushort GetGlyphId(ushort codePoint)
        {
            int segCount = EndCodes.Count;

            // Find the segment that contains the codePoint
            int segmentIndex = -1;
            for (var i = 0; i < segCount; i++)
            {
                if (codePoint > EndCodes[i]) continue;
                segmentIndex = i;
                break;
            }

            // If no segment is found or the codePoint is out of range, return 0
            if (segmentIndex == -1 || codePoint < StartCodes[segmentIndex])
            {
                return 0;
            }

            // Calculate the glyph index
            if (IdRangeOffsets[segmentIndex] == 0)
            {
                return (ushort)((codePoint + IdDeltas[segmentIndex]) % 65536);
            }
            int offset = IdRangeOffsets[segmentIndex] / 2 + (codePoint - StartCodes[segmentIndex]) - (segCount - segmentIndex);
            return GlyphIdArray[offset];
        }
    }
}