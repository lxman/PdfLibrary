using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Base
{
    public class BaseScriptTable
    {
        public BaseValuesTable BaseValuesTable { get; }

        public MinMaxTable DefaultMinMaxTable { get; }

        public List<BaseLangSysRecord> BaseLangSysRecords { get; } = new List<BaseLangSysRecord>();

        public BaseScriptTable(BigEndianReader reader)
        {
            long position = reader.Position;

            ushort baseValuesOffset = reader.ReadUShort();
            ushort defaultMinMaxOffset = reader.ReadUShort();
            ushort baseLangSysCount = reader.ReadUShort();
            for (var i = 0; i < baseLangSysCount; i++)
            {
                BaseLangSysRecords.Add(new BaseLangSysRecord(reader, position));
            }

            reader.Seek(position + baseValuesOffset);
            BaseValuesTable = new BaseValuesTable(reader);
            reader.Seek(position + defaultMinMaxOffset);
            DefaultMinMaxTable = new MinMaxTable(reader);
        }
    }
}