namespace FontParser.Tables.Cmap.SubTables
{
    public interface ICmapSubtable
    {
        int Language { get; }

        ushort GetGlyphId(ushort codePoint);
    }
}