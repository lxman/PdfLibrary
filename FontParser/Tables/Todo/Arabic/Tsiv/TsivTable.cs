using FontParser.Reader;

namespace FontParser.Tables.Todo.Arabic.Tsiv
{
    public class TsivTable : IFontTable
    {
        public static string Tag => "TSIV";

        public TsivTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            // TODO: Implement
            // TsivTable is a proprietary table in the Arabic script.
        }
    }
}