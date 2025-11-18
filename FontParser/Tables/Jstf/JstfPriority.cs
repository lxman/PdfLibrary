using FontParser.Reader;

namespace FontParser.Tables.Jstf
{
    public class JstfPriority
    {
        public JstfModList? GsubShrinkageEnable { get; }

        public JstfModList? GsubShrinkageDisable { get; }

        public JstfModList? GposShrinkageEnable { get; }

        public JstfModList? GposShrinkageDisable { get; }

        public JstfMax? ShrinkageJstfMax { get; }

        public JstfModList? GsubExtensionEnable { get; }

        public JstfModList? GsubExtensionDisable { get; }

        public JstfModList? GposExtensionEnable { get; }

        public JstfModList? GposExtensionDisable { get; }

        public JstfMax? ExtensionJstfMax { get; }

        public JstfPriority(BigEndianReader reader)
        {
            long start = reader.Position;

            ushort gsubShrinkageEnableOffset = reader.ReadUShort();
            ushort gsubShrinkageDisableOffset = reader.ReadUShort();
            ushort gposShrinkageEnableOffset = reader.ReadUShort();
            ushort gposShrinkageDisableOffset = reader.ReadUShort();
            ushort shrinkageJstfMaxOffset = reader.ReadUShort();
            ushort gsubExtensionEnableOffset = reader.ReadUShort();
            ushort gsubExtensionDisableOffset = reader.ReadUShort();
            ushort gposExtensionEnableOffset = reader.ReadUShort();
            ushort gposExtensionDisableOffset = reader.ReadUShort();
            ushort extensionJstfMaxOffset = reader.ReadUShort();
            if (gsubShrinkageEnableOffset != 0)
            {
                reader.Seek(start + gsubShrinkageEnableOffset);
                GsubShrinkageEnable = new JstfModList(reader);
            }
            if (gsubShrinkageDisableOffset != 0)
            {
                reader.Seek(start + gsubShrinkageDisableOffset);
                GsubShrinkageDisable = new JstfModList(reader);
            }
            if (gposShrinkageEnableOffset != 0)
            {
                reader.Seek(start + gposShrinkageEnableOffset);
                GposShrinkageEnable = new JstfModList(reader);
            }
            if (gposShrinkageDisableOffset != 0)
            {
                reader.Seek(start + gposShrinkageDisableOffset);
                GposShrinkageDisable = new JstfModList(reader);
            }
            if (shrinkageJstfMaxOffset != 0)
            {
                reader.Seek(start + shrinkageJstfMaxOffset);
                ShrinkageJstfMax = new JstfMax(reader);
            }
            if (gsubExtensionEnableOffset != 0)
            {
                reader.Seek(start + gsubExtensionEnableOffset);
                GsubExtensionEnable = new JstfModList(reader);
            }
            if (gsubExtensionDisableOffset != 0)
            {
                reader.Seek(start + gsubExtensionDisableOffset);
                GsubExtensionDisable = new JstfModList(reader);
            }
            if (gposExtensionEnableOffset != 0)
            {
                reader.Seek(start + gposExtensionEnableOffset);
                GposExtensionEnable = new JstfModList(reader);
            }
            if (gposExtensionDisableOffset != 0)
            {
                reader.Seek(start + gposExtensionDisableOffset);
                GposExtensionDisable = new JstfModList(reader);
            }
            if (extensionJstfMaxOffset == 0) return;
            reader.Seek(start + extensionJstfMaxOffset);
            ExtensionJstfMax = new JstfMax(reader);
        }
    }
}