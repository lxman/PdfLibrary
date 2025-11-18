using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Gpos.LookupSubtables.PairPos
{
    public class Class1Record
    {
        public ushort Class2RecordCount { get; }

        public List<Class2Record> Class2Records { get; } = new List<Class2Record>();

        public Class1Record(
            ushort class2RecordCount,
            ValueFormat vf1,
            ValueFormat vf2,
            BigEndianReader reader)
        {
            Class2RecordCount = class2RecordCount;
            for (var i = 0; i < Class2RecordCount; i++)
            {
                Class2Records.Add(new Class2Record(vf1, vf2, reader));
            }
        }
    }
}