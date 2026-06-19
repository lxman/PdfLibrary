using System;
using System.Collections.Generic;
using System.Text;
using FontParser.Reader;

namespace FontParser.Tables
{
    /// <summary>
    /// 'post' table — PostScript-oriented glyph metadata and, for format 2.0, an
    /// explicit per-glyph name list. The names enable the name→GID fallback the PDF
    /// spec requires when a font's cmap has no entry for a glyph's name
    /// (ISO 32000-2 §9.6.5.4).
    /// </summary>
    public class PostTable : IFontTable
    {
        public static string Tag => "post";

        /// <summary>Table version (1.0, 2.0, 2.5, 3.0…), read as Fixed 16.16.</summary>
        public float Version { get; }

        /// <summary>Italic angle in counter-clockwise degrees, read as Fixed 16.16.</summary>
        public float ItalicAngle { get; }

        public short UnderlinePosition { get; }

        public short UnderlineThickness { get; }

        public bool IsFixedPitch { get; }

        public uint MinMemType42 { get; }

        public uint MaxMemType42 { get; }

        public uint MinMemType1 { get; }

        public uint MaxMemType1 { get; }

        /// <summary>Glyph count carried by a format-2.0 post table (0 otherwise).</summary>
        public ushort NumGlyphs { get; }

        private readonly Dictionary<int, string> _gidToName = new();
        private readonly Dictionary<string, int> _nameToGid = new();

        /// <summary>Glyph-id → glyph-name map (format 2.0 only; empty otherwise).</summary>
        public IReadOnlyDictionary<int, string> GlyphNames => _gidToName;

        public PostTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            Version = reader.ReadF16Dot16();
            ItalicAngle = reader.ReadF16Dot16();
            UnderlinePosition = reader.ReadShort();
            UnderlineThickness = reader.ReadShort();
            IsFixedPitch = reader.ReadUInt32() != 0;
            MinMemType42 = reader.ReadUInt32();
            MaxMemType42 = reader.ReadUInt32();
            MinMemType1 = reader.ReadUInt32();
            MaxMemType1 = reader.ReadUInt32();

            // Only format 2.0 carries explicit per-glyph names. (1.0 = standard Mac order,
            // 3.0 = no names, 2.5 = deprecated.)
            if (Math.Abs(Version - 2.0f) > 0.001f) return;

            NumGlyphs = reader.ReadUShort();

            var glyphNameIndex = new ushort[NumGlyphs];
            for (var i = 0; i < NumGlyphs; i++)
            {
                glyphNameIndex[i] = reader.ReadUShort();
            }

            // Remaining bytes are Pascal strings (length byte + ASCII chars) for indices >= 258.
            var customNames = new List<string>();
            while (reader.BytesRemaining > 0)
            {
                byte length = reader.ReadByte();
                if (reader.BytesRemaining < length) break; // truncated/malformed — stop
                customNames.Add(Encoding.ASCII.GetString(reader.ReadBytes(length)));
            }

            for (var gid = 0; gid < NumGlyphs; gid++)
            {
                int nameIndex = glyphNameIndex[gid];
                string? name;
                if (nameIndex < MacintoshGlyphNames.StandardNameCount)
                {
                    name = MacintoshGlyphNames.GetStandardName(nameIndex);
                }
                else
                {
                    int customIndex = nameIndex - MacintoshGlyphNames.StandardNameCount;
                    name = customIndex >= 0 && customIndex < customNames.Count ? customNames[customIndex] : null;
                }

                if (name is null) continue;
                _gidToName[gid] = name;
                if (!_nameToGid.ContainsKey(name)) _nameToGid[name] = gid; // first GID wins
            }
        }

        /// <summary>Glyph name for a glyph id, or null if absent.</summary>
        public string? GetGlyphName(int glyphId) =>
            _gidToName.GetValueOrDefault(glyphId);

        /// <summary>Glyph id for a glyph name, or -1 if the name isn't in this table.</summary>
        public int GetGlyphIndex(string glyphName) =>
            _nameToGid.GetValueOrDefault(glyphName, -1);
    }
}
