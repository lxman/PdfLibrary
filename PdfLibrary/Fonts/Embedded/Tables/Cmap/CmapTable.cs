using PdfLibrary.Fonts.Embedded.Tables.Cmap.SubTables;

namespace PdfLibrary.Fonts.Embedded.Tables.Cmap
{
    /// <summary>
    /// TrueType 'cmap' table parser - character to glyph index mapping
    /// Supports all common formats: 0, 2, 4, 6, 10, 12, 13, 14
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

                    case 2:
                        subTable = new CmapSubtablesFormat2(reader);
                        SubTables.Add(subTable);
                        break;

                    case 4:
                        subTable = new CmapSubtablesFormat4(reader);
                        SubTables.Add(subTable);
                        break;

                    case 6:
                        subTable = new CmapSubtablesFormat6(reader);
                        SubTables.Add(subTable);
                        break;

                    case 10:
                        subTable = new CmapSubtablesFormat10(reader);
                        SubTables.Add(subTable);
                        break;

                    case 12:
                        subTable = new CmapSubtablesFormat12(reader);
                        SubTables.Add(subTable);
                        break;

                    case 13:
                        subTable = new CmapSubtablesFormat13(reader);
                        SubTables.Add(subTable);
                        break;

                    case 14:
                        subTable = new CmapSubtablesFormat14(reader);
                        SubTables.Add(subTable);
                        break;

                    // Format 8 is rare and not yet implemented
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
