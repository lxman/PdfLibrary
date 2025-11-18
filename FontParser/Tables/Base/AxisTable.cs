using FontParser.Reader;

namespace FontParser.Tables.Base
{
    public class AxisTable
    {
        public BaseTagListTable? BaseTagListTable { get; }

        public BaseScriptListTable? BaseScriptListTable { get; }

        public AxisTable(BigEndianReader reader)
        {
            long position = reader.Position;

            ushort baseTagListOffset = reader.ReadUShort();
            ushort baseScriptListOffset = reader.ReadUShort();
            if (baseTagListOffset > 0)
            {
                reader.Seek(baseTagListOffset + position);
                BaseTagListTable = new BaseTagListTable(reader);
            }

            if (baseScriptListOffset == 0) return;
            reader.Seek(baseScriptListOffset + position);
            BaseScriptListTable = new BaseScriptListTable(reader);
        }
    }
}