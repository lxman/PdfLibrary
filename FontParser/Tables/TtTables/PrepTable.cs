namespace FontParser.Tables.TtTables
{
    public class PrepTable : IFontTable
    {
        public static string Tag => "prep";

        public byte[] Instructions { get; }

        public PrepTable(byte[] data)
        {
            Instructions = data;
        }
    }
}