namespace PdfLibrary.Fonts.Embedded.Tables.Cmap.SubTables
{
    /// <summary>
    /// Cmap subtable format 12 - Segmented coverage (32-bit)
    /// Supports full Unicode range including supplementary planes (emoji, rare scripts)
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
    public class CmapSubtablesFormat12 : ICmapSubtable
    {
        public int Language { get; }

        public List<SequentialMapGroup> Groups { get; } = new List<SequentialMapGroup>();

        public CmapSubtablesFormat12(BigEndianReader reader)
        {
            ushort format = reader.ReadUShort(); // Should be 12
            _ = reader.ReadUShort(); // Reserved, always 0
            uint length = reader.ReadUInt32();
            Language = reader.ReadInt32();
            uint numGroups = reader.ReadUInt32();

            for (var i = 0; i < numGroups; i++)
            {
                Groups.Add(new SequentialMapGroup(reader.ReadBytes(SequentialMapGroup.RecordSize)));
            }
        }

        public ushort GetGlyphId(ushort codePoint)
        {
            // Find the group that contains this code point
            return (from @group in Groups
                    where codePoint >= @group.StartCharCode && codePoint <= @group.EndCharCode
                    select (ushort)(@group.StartGlyphId + (codePoint - @group.StartCharCode)))
                .FirstOrDefault();
        }
    }
}
