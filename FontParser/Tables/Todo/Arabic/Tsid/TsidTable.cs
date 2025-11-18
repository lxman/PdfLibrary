using FontParser.Reader;

namespace FontParser.Tables.Todo.Arabic.Tsid
{
    public class TsidTable : IFontTable
    {
        public static string Tag => "TSID";

        public TsidTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            // TODO: Implement
            // This is a proprietary table for Arabic fonts
        }
    }
}