using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Jstf
{
    public class JstfScript
    {
        public ExtenderGlyph? ExtenderGlyph { get; }

        public JstfLangSys? DefaultJstfLangSys { get; }

        public List<JstfLangSysRecord> JstfLangSysRecords { get; } = new List<JstfLangSysRecord>();

        public JstfScript(BigEndianReader reader)
        {
            long start = reader.Position;
            ushort extenderGlyphOffset = reader.ReadUShort();
            ushort defaultJstfLangSysOffset = reader.ReadUShort();
            ushort jstfLangSysCount = reader.ReadUShort();
            for (var i = 0; i < jstfLangSysCount; i++)
            {
                JstfLangSysRecords.Add(new JstfLangSysRecord(reader, start));
            }
            if (extenderGlyphOffset > 0)
            {
                reader.Seek(start + extenderGlyphOffset);
                ExtenderGlyph = new ExtenderGlyph(reader);
            }
            if (defaultJstfLangSysOffset > 0)
            {
                reader.Seek(start + defaultJstfLangSysOffset);
                DefaultJstfLangSys = new JstfLangSys(reader);
            }
        }
    }
}