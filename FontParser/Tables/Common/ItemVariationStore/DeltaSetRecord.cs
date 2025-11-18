using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Common.ItemVariationStore
{
    public class DeltaSetRecord
    {
        public List<int> DeltaData { get; } = new List<int>();

        public DeltaSetRecord(BigEndianReader reader, ushort regionIndexCount, bool useLongWords, int wordDeltaCount)
        {
            if (wordDeltaCount > regionIndexCount) return;
            for (var i = 0; i < wordDeltaCount; i++)
            {
                DeltaData.Add(useLongWords ? reader.ReadInt32() : reader.ReadInt16());
            }

            for (int i = wordDeltaCount; i < regionIndexCount; i++)
            {
                DeltaData.Add(useLongWords ? reader.ReadInt16() : reader.ReadSByte());
            }
        }
    }
}