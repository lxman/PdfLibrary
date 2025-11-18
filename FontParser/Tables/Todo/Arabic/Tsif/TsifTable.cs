using FontParser.Reader;

namespace FontParser.Tables.Todo.Arabic.Tsif
{
    public class TsifTable : IFontTable
    {
        public static string Tag => "TSIF";

        public TsifTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            // TODO: Implement TSIF table parsing
            // This is a proprietary table for Arabic fonts
        }
    }
}