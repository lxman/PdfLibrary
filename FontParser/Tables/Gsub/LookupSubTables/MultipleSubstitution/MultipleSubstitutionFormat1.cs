using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Common;
using FontParser.Tables.Common.CoverageFormat;

namespace FontParser.Tables.Gsub.LookupSubTables.MultipleSubstitution
{
    public class MultipleSubstitutionFormat1 : ILookupSubTable
    {
        public ICoverageFormat Coverage { get; }

        public List<SequenceTable> Sequences { get; } = new List<SequenceTable>();

        public MultipleSubstitutionFormat1(BigEndianReader reader)
        {
            long startOfTable = reader.Position;
            _ = reader.ReadUShort();
            ushort coverageOffset = reader.ReadUShort();
            ushort sequenceCount = reader.ReadUShort();
            ushort[] sequenceOffsets = reader.ReadUShortArray(sequenceCount);
            for (var i = 0; i < sequenceCount; i++)
            {
                reader.Seek(startOfTable + sequenceOffsets[i]);
                Sequences.Add(new SequenceTable(reader));
            }
            reader.Seek(startOfTable + coverageOffset);
            Coverage = CoverageTable.Retrieve(reader);
        }
    }
}