using System;
using System.Collections.Generic;

namespace PdfLibrary.Fonts.Embedded
{
    /// <summary>
    /// Minimal TrueType font parser to extract glyph names from 'post' table
    /// Supports TrueType (FontFile2) embedded fonts from PDFs
    /// </summary>
    public class TrueTypeParser
    {
        private class TableRecord
        {
            public string Tag { get; set; } = string.Empty;
            public uint Checksum { get; set; }
            public uint Offset { get; set; }
            public uint Length { get; set; }
        }

        /// <summary>
        /// Parse TrueType font and extract glyph names from 'post' table
        /// </summary>
        /// <param name="fontData">Raw TrueType font data from PDF FontFile2 stream</param>
        /// <returns>PostTable with glyph name mappings, or null if parsing fails</returns>
        public static PostTable? ParsePostTable(byte[] fontData)
        {
            try
            {
                using var reader = new BigEndianReader(fontData);

                // Read font header (TrueType or OpenType)
                uint sfntVersion = reader.ReadUInt32();

                // Check for TrueType (0x00010000) or OpenType (0x4F54544F = "OTTO")
                bool isTrueType = (sfntVersion == 0x00010000 || sfntVersion == 0x74727565); // 'true'
                bool isOpenType = (sfntVersion == 0x4F54544F); // 'OTTO'

                if (!isTrueType && !isOpenType)
                {
                    // Not a valid TrueType/OpenType font
                    return null;
                }

                // Read table directory
                ushort numTables = reader.ReadUShort();
                reader.ReadUShort(); // searchRange
                reader.ReadUShort(); // entrySelector
                reader.ReadUShort(); // rangeShift

                // Read table records
                var tables = new Dictionary<string, TableRecord>();
                for (int i = 0; i < numTables; i++)
                {
                    var record = new TableRecord
                    {
                        Tag = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4)),
                        Checksum = reader.ReadUInt32(),
                        Offset = reader.ReadUInt32(),
                        Length = reader.ReadUInt32()
                    };
                    tables[record.Tag] = record;
                }

                // Find 'post' table
                if (!tables.TryGetValue("post", out TableRecord? postRecord))
                {
                    // No 'post' table found
                    return null;
                }

                // Extract 'post' table data
                reader.Seek(postRecord.Offset);
                byte[] postData = reader.ReadBytes(postRecord.Length);

                // Parse 'post' table
                return new PostTable(postData);
            }
            catch
            {
                // Font parsing failed
                return null;
            }
        }

        /// <summary>
        /// Get glyph name for a character code (CID) from TrueType font
        /// </summary>
        /// <param name="fontData">Raw TrueType font data</param>
        /// <param name="glyphId">Glyph ID (same as CID for Type0 fonts)</param>
        /// <returns>Glyph name, or null if not found</returns>
        public static string? GetGlyphName(byte[] fontData, int glyphId)
        {
            PostTable? postTable = ParsePostTable(fontData);
            return postTable?.GetGlyphName(glyphId);
        }

        /// <summary>
        /// Get all glyph names from TrueType font
        /// </summary>
        public static Dictionary<int, string>? GetAllGlyphNames(byte[] fontData)
        {
            PostTable? postTable = ParsePostTable(fontData);
            if (postTable == null)
                return null;

            var result = new Dictionary<int, string>();
            foreach (var kvp in postTable.GetAllGlyphNames())
            {
                result[kvp.Key] = kvp.Value;
            }
            return result;
        }
    }
}
