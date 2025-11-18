using System.Collections.Generic;
using System.Text;
using FontParser.Reader;

namespace FontParser.Tables.Base
{
    public class BaseTagListTable
    {
        public List<string> BaselineTags { get; } = new List<string>();

        public BaseTagListTable(BigEndianReader reader)
        {
            ushort baseTagCount = reader.ReadUShort();
            for (var i = 0; i < baseTagCount; i++)
            {
                BaselineTags.Add(Encoding.UTF8.GetString(reader.ReadBytes(4)));
            }
        }
    }
}