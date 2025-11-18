namespace FontParser.Tables.Todo.Graphite.Silf
{
    public class SilfTable : IFontTable
    {
        public static string Tag => "Silf";

        public SilfTable(byte[] data)
        {
            // TODO: Implement this
            // This is a proprietary Graphite table
            // I have been unable to find any documentation on this table
        }
    }
}