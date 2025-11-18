using System.Collections.Generic;
using System.Linq;
using FontParser.Reader;

namespace FontParser.Tables.Name
{
    public class NameTable : IFontTable
    {
        public static string Tag => "name";

        public ushort Format { get; }

        public List<NameRecord> NameRecords { get; } = new List<NameRecord>();

        public List<LangTagRecord>? LangTagRecords { get; }

        public NameTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            Format = reader.ReadUShort();
            ushort count = reader.ReadUShort();
            ushort stringStorageOffset = reader.ReadUShort();
            for (var i = 0; i < count; i++)
            {
                NameRecords.Add(new NameRecord(reader.ReadBytes(NameRecord.RecordSize)));
            }
            NameRecords.ForEach(r => r.Process(reader, stringStorageOffset));
            if (Format == 0) return;
            ushort langTagCount = reader.ReadUShort();
            if (langTagCount == 0) return;
            LangTagRecords = new List<LangTagRecord>();
            for (var i = 0; i < langTagCount; i++)
            {
                LangTagRecords.Add(new LangTagRecord(reader.ReadBytes(LangTagRecord.RecordSize)));
            }
            LangTagRecords.ForEach(r => r.Process(reader, stringStorageOffset));
        }

        /// <summary>
        /// Gets the font family name (name ID 1)
        /// </summary>
        public string? GetFamilyName()
        {
            return NameRecords.FirstOrDefault(r => r.NameId == "Font Family name")?.Name;
        }

        /// <summary>
        /// Gets the PostScript name (name ID 6)
        /// </summary>
        public string? GetPostScriptName()
        {
            return NameRecords.FirstOrDefault(r => r.NameId == "PostScript name")?.Name;
        }
    }
}