using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Base
{
    public class BaseScriptListTable
    {
        public List<BaseScriptRecord> BaseScriptRecords { get; } = new List<BaseScriptRecord>();

        public BaseScriptListTable(BigEndianReader reader)
        {
            long position = reader.Position;

            ushort baseScriptCount = reader.ReadUShort();
            var tags = new List<byte[]>();
            var offsets = new List<ushort>();
            for (var i = 0; i < baseScriptCount; i++)
            {
                tags.Add(reader.ReadBytes(4));
                offsets.Add(reader.ReadUShort());
            }

            for (var i = 0; i < baseScriptCount; i++)
            {
                BaseScriptRecords.Add(new BaseScriptRecord(reader, position, tags[i], offsets[i]));
            }
        }
    }
}