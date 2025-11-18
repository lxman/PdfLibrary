using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Gpos.LookupSubtables.PairPos
{
    public class PairSet
    {
        public PairValueRecord[] PairValueRecords { get; }

        public PairSet(BigEndianReader reader, IList<ValueFormat> formats)
        {
            ushort pairValueCount = reader.ReadUShort();
            PairValueRecords = new PairValueRecord[pairValueCount];
            for (var i = 0; i < pairValueCount; i++)
            {
                PairValueRecords[i] = new PairValueRecord(reader, formats);
            }
        }
    }
}