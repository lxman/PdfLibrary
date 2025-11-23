using System.Text;
using FontParser.Reader;

namespace PdfLibrary.Fonts.Embedded
{
    /// <summary>
    /// TrueType font parser for extracting tables from embedded fonts
    /// Supports TrueType (FontFile2) and OpenType embedded fonts from PDFs
    /// </summary>
    public class TrueTypeParser
    {
        private readonly Dictionary<string, TableRecord> _tables;
        private readonly byte[] _fontData;

        private class TableRecord
        {
            public string Tag { get; set; } = string.Empty;
            public uint Checksum { get; set; }
            public uint Offset { get; set; }
            public uint Length { get; set; }
        }

        /// <summary>
        /// Creates a parser for the given TrueType/OpenType font data
        /// </summary>
        public TrueTypeParser(byte[] fontData)
        {
            _fontData = fontData ?? throw new ArgumentNullException(nameof(fontData));
            _tables = ParseTableDirectory(fontData);
        }

        /// <summary>
        /// Gets the raw data for a specific table by tag
        /// </summary>
        /// <param name="tag">Four-character table tag (e.g., "head", "hhea", "hmtx")</param>
        /// <returns>Table data bytes, or null if table not found</returns>
        public byte[]? GetTable(string tag)
        {
            if (!_tables.TryGetValue(tag, out TableRecord? record))
                return null;

            try
            {
                using var reader = new BigEndianReader(_fontData);
                reader.Seek(record.Offset);
                return reader.ReadBytes(record.Length);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Checks if a specific table exists in the font
        /// </summary>
        public bool HasTable(string tag) => _tables.ContainsKey(tag);

        /// <summary>
        /// Gets all available table tags
        /// </summary>
        public IEnumerable<string> GetTableTags() => _tables.Keys;

        /// <summary>
        /// Parses the TrueType table directory
        /// </summary>
        private static Dictionary<string, TableRecord> ParseTableDirectory(byte[] fontData)
        {
            var tables = new Dictionary<string, TableRecord>();

            try
            {
                using var reader = new BigEndianReader(fontData);

                // Read font header (TrueType or OpenType)
                uint sfntVersion = reader.ReadUInt32();

                // Check for TrueType (0x00010000) or OpenType (0x4F54544F = "OTTO") or 'true'
                bool isValid = (sfntVersion == 0x00010000 || sfntVersion == 0x74727565 || sfntVersion == 0x4F54544F);

                if (!isValid)
                    return tables; // Not a valid font

                // Read table directory header
                ushort numTables = reader.ReadUShort();
                reader.ReadUShort(); // searchRange
                reader.ReadUShort(); // entrySelector
                reader.ReadUShort(); // rangeShift

                // Read table records
                for (var i = 0; i < numTables; i++)
                {
                    var record = new TableRecord
                    {
                        Tag = Encoding.ASCII.GetString(reader.ReadBytes(4)),
                        Checksum = reader.ReadUInt32(),
                        Offset = reader.ReadUInt32(),
                        Length = reader.ReadUInt32()
                    };
                    tables[record.Tag] = record;
                }
            }
            catch
            {
                // Failed to parse table directory
            }

            return tables;
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
                var parser = new TrueTypeParser(fontData);
                byte[]? postData = parser.GetTable("post");
                return postData != null ? new PostTable(postData) : null;
            }
            catch
            {
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
            foreach (KeyValuePair<int, string> kvp in postTable.GetAllGlyphNames())
            {
                result[kvp.Key] = kvp.Value;
            }
            return result;
        }
    }
}
