using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Pfed.SubTables
{
    public class CmntItem
    {
        public ushort StartingGlyphIndex { get; }

        public ushort EndingGlyphIndex { get; }

        public List<string> Comments { get; } = new List<string>();

        public CmntItem(BigEndianReader reader, ushort version)
        {
            long start = reader.Position;

            StartingGlyphIndex = reader.ReadUShort();
            EndingGlyphIndex = reader.ReadUShort();
            uint offset = reader.ReadUInt32();
            int entryCount = EndingGlyphIndex - StartingGlyphIndex + 2;
            reader.Seek(start + offset);
            var offsets = new List<uint>();
            for (var i = 0; i < entryCount; i++)
            {
                offsets.Add(reader.ReadUInt32());
            }
            for (var i = 0; i < entryCount - 1; i++)
            {
                reader.Seek(start + offsets[i]);
                byte[] comment = reader.ReadBytes((int)(offsets[i + 1] - offsets[i]));
                switch (version)
                {
                    case 0:
                        Comments.Add(System.Text.Encoding.BigEndianUnicode.GetString(comment));
                        break;

                    case 1:
                        Comments.Add(System.Text.Encoding.UTF8.GetString(comment));
                        break;
                }
            }
        }
    }
}