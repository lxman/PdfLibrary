using FontParser.Reader;

namespace FontParser.Tables.Cmap.SubTables
{
    public class VariationSelectorRecord
    {
        public uint VarSelector { get; }

        public DefaultUvsTableHeader? DefaultUvsTableHeader { get; private set; }

        public NonDefaultUvsTableHeader? NonDefaultUvsTableHeader { get; private set; }

        private readonly long _tableStart;
        private readonly uint _defaultUvsOffset;
        private readonly uint _nonDefaultUvsOffset;

        public VariationSelectorRecord(BigEndianReader reader, long tableStart)
        {
            _tableStart = tableStart;
            VarSelector = reader.ReadUInt24();
            _defaultUvsOffset = reader.ReadUInt32();
            _nonDefaultUvsOffset = reader.ReadUInt32();
        }

        public void Process(BigEndianReader reader)
        {
            if (_defaultUvsOffset > 0)
            {
                reader.Seek(_tableStart + _defaultUvsOffset);
                DefaultUvsTableHeader = new DefaultUvsTableHeader(reader);
            }

            if (_nonDefaultUvsOffset == 0) return;
            reader.Seek(_tableStart + _nonDefaultUvsOffset);
            NonDefaultUvsTableHeader = new NonDefaultUvsTableHeader(reader);
        }
    }
}