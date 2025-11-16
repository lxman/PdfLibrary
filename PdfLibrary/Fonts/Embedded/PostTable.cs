using System.Text;

namespace PdfLibrary.Fonts.Embedded
{
    /// <summary>
    /// TrueType 'post' table parser - extracts glyph names from embedded fonts
    /// Format 2.0 only (most common format with explicit glyph names)
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
    public class PostTable
    {
        public float Version { get; }
        public ushort NumGlyphs { get; }

        private readonly Dictionary<int, string> _glyphNames = new Dictionary<int, string>();

        public PostTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            // Read header
            Version = reader.ReadF16Dot16(); // Fixed 16.16 format

            // Skip metrics we don't need for glyph name extraction
            reader.Seek(8);  // Skip italicAngle (4 bytes) + underlinePosition (2) + underlineThickness (2)
            reader.ReadUInt32(); // isFixedPitch
            reader.ReadUInt32(); // minMemType42
            reader.ReadUInt32(); // maxMemType42
            reader.ReadUInt32(); // minMemType1
            reader.ReadUInt32(); // maxMemType1

            // Only format 2.0 has explicit glyph names
            if (Math.Abs(Version - 2.0f) > 0.001f)
            {
                // Format 1.0 uses standard Mac names (258 glyphs)
                // Format 2.5 is deprecated
                // Format 3.0 has no glyph names
                return;
            }

            // Read format 2.0 glyph names
            NumGlyphs = reader.ReadUShort();

            // Read glyph name indices
            var glyphNameIndex = new ushort[NumGlyphs];
            for (int i = 0; i < NumGlyphs; i++)
            {
                glyphNameIndex[i] = reader.ReadUShort();
            }

            // Read custom glyph names (Pascal strings)
            var customNames = new List<string>();
            while (reader.BytesRemaining > 0)
            {
                try
                {
                    byte length = reader.ReadByte();
                    if (length == 0)
                        break; // End of names

                    if (reader.BytesRemaining < length)
                        break; // Malformed data

                    byte[] nameBytes = reader.ReadBytes(length);
                    string name = Encoding.ASCII.GetString(nameBytes);
                    customNames.Add(name);
                }
                catch
                {
                    break; // Malformed data
                }
            }

            // Build glyph ID → name mapping
            for (int glyphId = 0; glyphId < NumGlyphs; glyphId++)
            {
                int nameIndex = glyphNameIndex[glyphId];

                if (nameIndex < MacintoshGlyphNames.StandardNameCount)
                {
                    // Standard Macintosh name (0-257)
                    string? stdName = MacintoshGlyphNames.GetStandardName(nameIndex);
                    if (stdName != null)
                    {
                        _glyphNames[glyphId] = stdName;
                    }
                }
                else
                {
                    // Custom name (258+)
                    int customIndex = nameIndex - MacintoshGlyphNames.StandardNameCount;
                    if (customIndex >= 0 && customIndex < customNames.Count)
                    {
                        _glyphNames[glyphId] = customNames[customIndex];
                    }
                }
            }
        }

        /// <summary>
        /// Get glyph name for a given glyph ID
        /// </summary>
        public string? GetGlyphName(int glyphId)
        {
            return _glyphNames.TryGetValue(glyphId, out string? name) ? name : null;
        }

        /// <summary>
        /// Get all glyph names as a dictionary (glyph ID → name)
        /// </summary>
        public IReadOnlyDictionary<int, string> GetAllGlyphNames()
        {
            return _glyphNames;
        }
    }
}
