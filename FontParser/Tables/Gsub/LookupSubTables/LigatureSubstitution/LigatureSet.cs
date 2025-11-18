using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Gsub.LookupSubTables.LigatureSubstitution
{
    public class LigatureSet
    {
        public List<Ligature> LigatureTables { get; } = new List<Ligature>();

        public LigatureSet(BigEndianReader reader)
        {
            long startOfTable = reader.Position;
            ushort ligatureCount = reader.ReadUShort();
            ushort[] ligatureOffsets = reader.ReadUShortArray(ligatureCount);
            for (var i = 0; i < ligatureCount; i++)
            {
                reader.Seek(startOfTable + ligatureOffsets[i]);
                LigatureTables.Add(new Ligature(reader));
            }
        }
    }
}