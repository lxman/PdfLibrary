using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Common;

namespace FontParser.Tables.Gpos.LookupSubtables.PairPos
{
    public class PairValueRecord
    {
        public ushort SecondGlyph { get; }

        public ValueRecord Value1 { get; }

        public ValueRecord Value2 { get; }

        public PairValueRecord(BigEndianReader reader, IList<ValueFormat> formats)
        {
            SecondGlyph = reader.ReadUShort();
            Value1 = new ValueRecord(formats[0], reader);
            Value2 = new ValueRecord(formats[1], reader);
        }
    }
}