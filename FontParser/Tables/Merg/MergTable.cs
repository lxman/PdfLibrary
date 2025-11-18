using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Common.ClassDefinition;

namespace FontParser.Tables.Merg
{
    public class MergTable : IFontTable
    {
        public static string Tag => "MERG";

        public ushort Version { get; }

        public List<IClassDefinition> ClassDefinitions { get; } = new List<IClassDefinition>();

        public List<MergeEntry> MergeEntries { get; } = new List<MergeEntry>();

        public MergTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            Version = reader.ReadUShort();
            ushort mergeClassCount = reader.ReadUShort();
            ushort mergeDataOffset = reader.ReadUShort();
            ushort classDefCount = reader.ReadUShort();
            ushort offsetToClassDefOffsets = reader.ReadUShort();
            if (offsetToClassDefOffsets != 0)
            {
                reader.Seek(offsetToClassDefOffsets);
                ushort[] classDefOffsets = reader.ReadUShortArray(classDefCount);
                for (var i = 0; i < classDefCount; i++)
                {
                    reader.Seek(classDefOffsets[i]);
                    ushort format = reader.PeekBytes(2)[1];
                    switch (format)
                    {
                        case 1:
                            ClassDefinitions.Add(new ClassDefinitionFormat1(reader));
                            break;

                        case 2:
                            ClassDefinitions.Add(new ClassDefinitionFormat2(reader));
                            break;
                    }
                }
            }
            if (mergeDataOffset == 0 || mergeClassCount == 0)
            {
                return;
            }
            reader.Seek(mergeDataOffset);
            for (var i = 0; i < mergeClassCount; i++)
            {
                MergeEntries.Add(new MergeEntry(reader, mergeClassCount));
            }
        }
    }
}