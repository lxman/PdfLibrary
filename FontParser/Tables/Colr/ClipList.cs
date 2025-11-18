using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Colr
{
    public class ClipList
    {
        public byte Format { get; }

        public List<ClipRecord> ClipRecords { get; } = new List<ClipRecord>();

        public ClipList(BigEndianReader reader)
        {
            Format = reader.ReadByte();
            ushort clipRecordCount = reader.ReadUShort();
            for (var i = 0; i < clipRecordCount; i++)
            {
                ClipRecords.Add(new ClipRecord(reader));
            }
        }
    }
}