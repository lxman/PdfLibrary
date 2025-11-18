using FontParser.Reader;

namespace FontParser.Tables.Common
{
    public class ScriptList
    {
        public ScriptRecord[] ScriptRecords { get; }

        public ScriptList(BigEndianReader reader)
        {
            long start = reader.Position;

            ushort scriptCount = reader.ReadUShort();

            ScriptRecords = new ScriptRecord[scriptCount];
            for (var i = 0; i < scriptCount; i++)
            {
                ScriptRecords[i] = new ScriptRecord(reader, start);
            }
        }
    }
}