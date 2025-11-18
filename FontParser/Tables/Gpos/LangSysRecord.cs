using System.Text;
using FontParser.Reader;

namespace FontParser.Tables.Gpos
{
    public class LangSysRecord
    {
        public string LangSysTag { get; }

        public ushort LangSysOffset { get; }

        public LangSysRecord(BigEndianReader reader)
        {
            byte[] tag = reader.ReadBytes(4);
            LangSysTag = Encoding.ASCII.GetString(tag);

            LangSysOffset = reader.ReadUShort();
        }
    }
}