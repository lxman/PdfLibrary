namespace FontParser.Tables.Todo.Graphite.Glat
{
    public class GlatTable : IFontTable
    {
        public static string Tag => "Glat";

        public GlatTable(byte[] data)
        {
            // TODO: Implement GLAT table parsing
            // GLAT table is a Graphite table that contains glyph attributes
            // I have been unable to find any documentation on this table
        }
    }
}