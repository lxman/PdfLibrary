using System.Collections.Generic;
using System.Linq;

namespace PdfLibrary.Fonts.Embedded.Tables
{
    /// <summary>
    /// TrueType 'cmap' table parser - character to glyph index mapping
    /// Currently supports Format 4 (Unicode BMP - most common in PDFs)
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
    public class CmapTable
    {
        public static string Tag => "cmap";

        public ushort Version { get; }

        public List<CmapEncoding> Encodings { get; } = new List<CmapEncoding>();

        public List<EncodingRecord> EncodingRecords { get; } = new List<EncodingRecord>();

        public List<ICmapSubtable> SubTables { get; } = new List<ICmapSubtable>();

        public CmapTable(byte[] cmapData)
        {
            var reader = new BigEndianReader(cmapData);
            Version = reader.ReadUShort();
            ushort numTables = reader.ReadUShort();

            // Read encoding records
            for (var i = 0; i < numTables; i++)
            {
                EncodingRecords.Add(new EncodingRecord(reader.ReadBytes(EncodingRecord.RecordSize)));
            }

            // Sort by offset to process in order
            EncodingRecords = EncodingRecords.OrderBy(x => x.Offset).ToList();

            // Parse subtables
            foreach (EncodingRecord encodingRecord in EncodingRecords)
            {
                reader.Seek(encodingRecord.Offset);
                ushort format = reader.ReadUShort();

                // Seek back to start of subtable for proper parsing
                reader.Seek(encodingRecord.Offset);

                ICmapSubtable? subTable = null;
                switch (format)
                {
                    case 0:
                        subTable = new CmapSubtablesFormat0(reader);
                        SubTables.Add(subTable);
                        break;

                    case 4:
                        subTable = new CmapSubtablesFormat4(reader);
                        SubTables.Add(subTable);
                        break;

                    // Add more formats here as needed (6, 12 are common)
                    default:
                        // Skip unsupported formats
                        break;
                }

                if (subTable != null)
                {
                    Encodings.Add(new CmapEncoding(encodingRecord, subTable));
                }
            }
        }

        /// <summary>
        /// Get glyph ID for a Unicode code point
        /// </summary>
        public ushort GetGlyphId(ushort codePoint)
        {
            return SubTables
                .Select(subTable => subTable.GetGlyphId(codePoint))
                .FirstOrDefault(glyphId => glyphId != 0);
        }

        /// <summary>
        /// Get the best Unicode cmap encoding (prefers Windows Unicode)
        /// </summary>
        public CmapEncoding? GetPreferredUnicodeEncoding()
        {
            // Prefer Windows Unicode BMP
            var windowsUnicode = Encodings.FirstOrDefault(e =>
                e.Encoding.PlatformId == PlatformId.Windows &&
                e.Encoding.WindowsEncoding == WindowsEncodingId.UnicodeBmp);
            if (windowsUnicode != null)
                return windowsUnicode;

            // Fallback to Unicode platform
            return Encodings.FirstOrDefault(e => e.Encoding.PlatformId == PlatformId.Unicode);
        }
    }
}
