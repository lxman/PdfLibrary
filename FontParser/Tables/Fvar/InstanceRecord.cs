using FontParser.Reader;

namespace FontParser.Tables.Fvar
{
    public class InstanceRecord
    {
        public ushort SubfamilyNameId { get; }

        public ushort Flags { get; }

        public UserTuple Coordinates { get; }

        public ushort PostScriptNameId { get; }

        public InstanceRecord(BigEndianReader reader, ushort axisCount, ushort instanceSize)
        {
            long start = reader.Position;

            SubfamilyNameId = reader.ReadUShort();
            Flags = reader.ReadUShort();
            Coordinates = new UserTuple(reader, axisCount);
            if (instanceSize > reader.Position - start)
            {
                PostScriptNameId = reader.ReadUShort();
            }
        }
    }
}