using FontParser.Reader;

namespace FontParser.Tables.Todo.Arabic.Tsip
{
    public class TsipTable : IFontTable
    {
        public static string Tag => "TSIP";

        public TsipTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            // TODO: Implement TSIP table parsing
            // TSIP table is a proprietary table used by Arabic fonts.
        }
    }
}