using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Optional.Hdmx
{
    public class HdmxTable : IFontTable
    {
        public static string Tag => "hdmx";

        public ushort Version { get; private set; }

        public List<HdmxRecord> Records { get; } = new List<HdmxRecord>();

        private readonly BigEndianReader _reader;

        public HdmxTable(byte[] data)
        {
            _reader = new BigEndianReader(data);
        }

        // numGlyphs is from the maxp table
        public void Process(ushort numGlyphs)
        {
            Version = _reader.ReadUShort();
            short numRecords = _reader.ReadShort();
            int recordSize = _reader.ReadInt32();

            for (var i = 0; i < numRecords; i++)
            {
                Records.Add(new HdmxRecord(_reader, numGlyphs));
            }
        }
    }
}