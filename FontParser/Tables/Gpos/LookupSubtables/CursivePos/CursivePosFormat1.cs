using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Common;
using FontParser.Tables.Common.CoverageFormat;

namespace FontParser.Tables.Gpos.LookupSubtables.CursivePos
{
    public class CursivePosFormat1 : ILookupSubTable
    {
        public ushort Format { get; }

        public ICoverageFormat Coverage { get; }

        public List<EntryExitRecord> EntryExitRecords { get; } = new List<EntryExitRecord>();

        public CursivePosFormat1(BigEndianReader reader)
        {
            long startOfTable = reader.Position;
            Format = reader.ReadUShort();
            ushort coverageOffset = reader.ReadUShort();
            ushort entryExitCount = reader.ReadUShort();
            long before = reader.Position;
            for (var i = 0; i < entryExitCount; i++)
            {
                EntryExitRecords.Add(new EntryExitRecord(reader, startOfTable));
            }
            reader.Seek(startOfTable + coverageOffset);
            Coverage = CoverageTable.Retrieve(reader);
            reader.Seek(before);
        }
    }
}