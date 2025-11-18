using System.Text;
using FontParser.Reader;

namespace FontParser.Tables.Jstf
{
    public class JstfLangSysRecord
    {
        public string LangSysTag { get; }

        public JstfLangSys JstfLangSys { get; }

        public JstfLangSysRecord(BigEndianReader reader, long jstfStart)
        {
            LangSysTag = Encoding.ASCII.GetString(reader.ReadBytes(4));
            ushort jstfLangSysOffset = reader.ReadUShort();
            reader.Seek(jstfStart + jstfLangSysOffset);
            JstfLangSys = new JstfLangSys(reader);
        }
    }
}