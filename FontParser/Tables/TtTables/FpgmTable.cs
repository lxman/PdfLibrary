namespace FontParser.Tables.TtTables
{
    public class FpgmTable : IFontTable
    {
        public static string Tag => "fpgm";

        public byte[] Instructions { get; }

        public FpgmTable(byte[] data)
        {
            // Subtract 4 bytes for the fuzz factor
            Instructions = data[..^4];
        }
    }
}