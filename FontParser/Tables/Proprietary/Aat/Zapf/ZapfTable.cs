using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Zapf
{
    public class ZapfTable : IFontTable
    {
        public static string Tag => "Zapf";

        public ZapfTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);
        }
    }
}