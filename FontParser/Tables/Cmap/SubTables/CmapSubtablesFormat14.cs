using System.Collections.Generic;
using System.Linq;
using FontParser.Reader;

namespace FontParser.Tables.Cmap.SubTables
{
    public class CmapSubtablesFormat14 : ICmapSubtable
    {
        public int Language => -1;

        public List<VariationSelectorRecord> VarSelectorRecords { get; } = new List<VariationSelectorRecord>();

        public CmapSubtablesFormat14(BigEndianReader reader)
        {
            long position = reader.Position;

            ushort format = reader.ReadUShort();
            uint length = reader.ReadUInt32();
            uint numVarSelectorRecords = reader.ReadUInt32();
            for (var i = 0; i < numVarSelectorRecords; i++)
            {
                VarSelectorRecords.Add(new VariationSelectorRecord(reader, position));
            }
            reader.Seek(position);
            for (var i = 0; i < numVarSelectorRecords; i++)
            {
                VarSelectorRecords[i].Process(reader);
            }
        }

        public ushort GetGlyphId(ushort codePoint)
        {
            foreach (VariationSelectorRecord? record in VarSelectorRecords)
            {
                if (!(record.DefaultUvsTableHeader is null))
                {
                    if ((from range in record.DefaultUvsTableHeader.UnicodeRangeRecords
                         let endUnicodeValue = range.StartUnicodeValue + range.AdditionalCount
                         where codePoint >= range.StartUnicodeValue && codePoint <= endUnicodeValue
                         select range)
                        .Any())
                    {
                        return 0; // Default UVS returns 0 for the base glyph
                    }
                }

                if (record.NonDefaultUvsTableHeader is null) continue;
                foreach (UvsMappingRecord? mapping in record.NonDefaultUvsTableHeader.UvsMappings.Where(mapping => codePoint == mapping.UnicodeValue))
                {
                    return mapping.GlyphId;
                }
            }
            return 0;
        }
    }
}