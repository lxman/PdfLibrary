using FontParser.Reader;

namespace FontParser.Tables.Gpos
{
    public class ScriptTable
    {
        public LangSysRecord[] ScriptRecords { get; }

        public ScriptTable(BigEndianReader reader, long offset)
        {
            reader.Seek(offset);

            ushort defaultLangSysOffset = reader.ReadUShort();
            ushort scriptCount = reader.ReadUShort();

            ScriptRecords = new LangSysRecord[scriptCount];
            for (var i = 0; i < scriptCount; i++)
            {
                ScriptRecords[i] = new LangSysRecord(reader);
            }
        }
    }
}