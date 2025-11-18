namespace PdfLibrary.Fonts.Embedded.Tables.Cmap.SubTables
{
    /// <summary>
    /// Cmap subtable format 13 - Many-to-one range mappings
    /// Maps contiguous character ranges to a single glyph ID
    /// Used for fonts where many characters map to the same glyph (.notdef)
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
    public class CmapSubtablesFormat13 : ICmapSubtable
    {
        public int Language { get; }

        public List<ConstantMapGroup> Groups { get; } = new List<ConstantMapGroup>();

        public CmapSubtablesFormat13(BigEndianReader reader)
        {
            ushort format = reader.ReadUShort();
            _ = reader.ReadUShort();
            uint length = reader.ReadUInt32();
            Language = reader.ReadInt32();
            int numGroups = reader.ReadInt32();
            for (var i = 0; i < numGroups; i++)
            {
                Groups.Add(new ConstantMapGroup(reader.ReadBytes(ConstantMapGroup.RecordSize)));
            }
        }

        public ushort GetGlyphId(ushort codePoint)
        {
            return (from @group in Groups
                    where codePoint >= @group.StartCharCode && codePoint <= @group.EndCharCode
                    select (ushort)@group.GlyphId)
                .FirstOrDefault();
        }
    }
}
