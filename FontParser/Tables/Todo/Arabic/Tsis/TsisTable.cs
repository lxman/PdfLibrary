using FontParser.Reader;

namespace FontParser.Tables.Todo.Arabic.Tsis
{
    public class TsisTable : IFontTable
    {
        public static string Tag => "TSIS";

        public TsisTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            // TODO: Implement TsisTable
            // This is a proprietary table that seems to be for Arabic fonts
        }
    }
}