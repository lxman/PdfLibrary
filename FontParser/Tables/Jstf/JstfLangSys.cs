using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Jstf
{
    public class JstfLangSys
    {
        public List<JstfPriority> JstfPriorities { get; } = new List<JstfPriority>();

        public JstfLangSys(BigEndianReader reader)
        {
            long start = reader.Position;
            ushort jstfPriorityCount = reader.ReadUShort();
            ushort[] jstfPriorityOffsets = reader.ReadUShortArray(jstfPriorityCount);
            for (var i = 0; i < jstfPriorityCount; i++)
            {
                reader.Seek(start + jstfPriorityOffsets[i]);
                JstfPriorities.Add(new JstfPriority(reader));
            }
        }
    }
}