using System.Collections.Generic;
using System.Linq;

namespace PdfLibrary.Fonts.Embedded.Tables
{
    /// <summary>
    /// TrueType 'name' table parser - font naming information
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
    public class NameTable
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
        /// Get name string by NameId (e.g., "Family", "Full Name", "PostScript Name")
        /// </summary>
        public string? GetName(string nameId)
        {
            // Prefer Windows platform, Unicode encoding (most common in PDFs)
            var windowsRecord = NameRecords.FirstOrDefault(r =>
                r.PlatformId == PlatformId.Windows && r.NameId == nameId);
            if (windowsRecord?.Name != null)
                return windowsRecord.Name;

            // Fallback to any platform with matching nameId
            return NameRecords.FirstOrDefault(r => r.NameId == nameId)?.Name;
        }

        /// <summary>
        /// Get font family name (NameID 1)
        /// </summary>
        public string? GetFamilyName() => GetName("Family");

        /// <summary>
        /// Get font subfamily name (NameID 2) - e.g., "Regular", "Bold", "Italic"
        /// </summary>
        public string? GetSubfamilyName() => GetName("Subfamily");

        /// <summary>
        /// Get full font name (NameID 4)
        /// </summary>
        public string? GetFullName() => GetName("Full Name");

        /// <summary>
        /// Get PostScript name (NameID 6)
        /// </summary>
        public string? GetPostScriptName() => GetName("PostScript Name");
    }
}
