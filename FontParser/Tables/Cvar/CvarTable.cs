using System;
using FontParser.Reader;
using FontParser.Tables.Common.TupleVariationStore;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace FontParser.Tables.Cvar
{
    public class CvarTable : IFontTable
    {
        public static string Tag => "cvar";

        public Header Header { get; private set; }

        private readonly BigEndianReader _reader;

        public CvarTable(byte[] data)
        {
            _reader = new BigEndianReader(data);
        }

        public void Process(int axisCount)
        {
            Header = new Header(_reader, Convert.ToUInt16(axisCount), true);
        }
    }
}