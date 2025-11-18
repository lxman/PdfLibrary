using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Common;
using FontParser.Tables.Common.CoverageFormat;

namespace FontParser.Tables.Gpos.LookupSubtables.PairPos
{
    public class PairPosFormat1 : ILookupSubTable
    {
        public ushort PosFormat { get; }

        public ICoverageFormat Coverage { get; }

        public ValueFormat ValueFormat1 { get; }

        public ValueFormat ValueFormat2 { get; }

        public PairSet[] PairSets { get; }

        public PairPosFormat1(BigEndianReader reader)
        {
            long startOfTable = reader.Position;
            PosFormat = reader.ReadUShort();
            ushort coverageOffset = reader.ReadUShort();
            ValueFormat1 = (ValueFormat)reader.ReadShort();
            ValueFormat2 = (ValueFormat)reader.ReadShort();
            ushort pairSetCount = reader.ReadUShort();
            ushort[] pairSetOffsets = reader.ReadUShortArray(pairSetCount);
            PairSets = new PairSet[pairSetCount];
            for (var i = 0; i < pairSetCount; i++)
            {
                reader.Seek(startOfTable + pairSetOffsets[i]);
                PairSets[i] = new PairSet(reader, new List<ValueFormat> { ValueFormat1, ValueFormat2 });
            }
            reader.Seek(startOfTable + coverageOffset);
            Coverage = CoverageTable.Retrieve(reader);
        }
    }
}