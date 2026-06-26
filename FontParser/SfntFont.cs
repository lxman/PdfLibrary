using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FontParser.Reader;
using FontParser.Tables;
using FontParser.Tables.Cff.Type1;
using FontParser.Tables.Cmap;
using FontParser.Tables.Head;
using FontParser.Tables.Hhea;
using FontParser.Tables.Hmtx;
using FontParser.Tables.Name;
using FontParser.Tables.TtTables;
using FontParser.Tables.TtTables.Glyf;

namespace FontParser
{
    public enum SfntOutlineKind
    {
        Unknown,
        TrueType, // 'glyf' outlines
        Cff       // 'CFF ' (Type1C / Type2 charstrings)
    }

    /// <summary>
    /// Parses a single bare sfnt font program (a TrueType 'glyf' font or an OpenType
    /// 'CFF ' font) from its raw bytes and exposes its tables. This is the lightweight,
    /// synchronous entry point that replaces the removed FontReader/FontStructure: no
    /// reflection, no Parallel.ForEach, no padding, exact table lengths. Table accessors
    /// are lazy and cached, and they perform the cross-table Process() wiring (loca/glyf
    /// need glyph count + index format; hmtx needs the metric count) so callers get
    /// ready-to-use tables.
    ///
    /// Scope is deliberately PDF-shaped: a PDF embeds exactly one font program. A TrueType
    /// Collection ('ttcf') — which can appear when locating a system font for a non-embedded
    /// face — is handled by selecting the requested face by index (font 0 by default); WOFF/WOFF2 containers are not supported.
    /// </summary>
    public sealed class SfntFont
    {
        private readonly byte[] _data;
        private readonly Dictionary<string, (uint Offset, uint Length)> _directory = new();

        public SfntOutlineKind OutlineKind { get; }

        /// <summary>Number of faces: 1 for a single font, or the font count for a TTC collection.</summary>
        public int FaceCount { get; }

        public SfntFont(byte[] data) : this(data, 0) { }

        public SfntFont(byte[] data, int faceIndex)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            if (faceIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(faceIndex), "Face index must be non-negative.");

            using var reader = new BigEndianReader(data);
            uint sfntVersion = reader.ReadUInt32();

            if (sfntVersion == 0x74746366) // 'ttcf'
            {
                reader.ReadUShort();            // majorVersion
                reader.ReadUShort();            // minorVersion
                uint numFonts = reader.ReadUInt32();
                if (numFonts == 0)
                    throw new InvalidDataException("TrueType collection (ttcf) declares zero fonts.");
                if (faceIndex >= numFonts)
                    throw new ArgumentOutOfRangeException(nameof(faceIndex),
                        $"Face {faceIndex} requested but the collection has {numFonts} font(s).");
                FaceCount = (int)numFonts;

                long offsetTablePos = reader.Position;          // start of the uint32 offset array
                reader.Seek(offsetTablePos + (long)faceIndex * 4);
                uint fontOffset = reader.ReadUInt32();           // offset to this face's table directory
                reader.Seek(fontOffset);
                sfntVersion = reader.ReadUInt32();               // the real sfnt version of this face
            }
            else
            {
                if (faceIndex != 0)
                    throw new ArgumentOutOfRangeException(nameof(faceIndex),
                        "This is a single font program; only face 0 exists.");
                FaceCount = 1;
            }

            // 0x00010000 = TrueType outlines, 'true' = legacy TrueType, 'OTTO' = CFF outlines.
            if (sfntVersion != 0x00010000 && sfntVersion != 0x74727565 && sfntVersion != 0x4F54544F)
            {
                throw new InvalidDataException(
                    $"Not a supported sfnt font program (sfnt version 0x{sfntVersion:X8}). " +
                    "WOFF/WOFF2 containers are not supported.");
            }

            ushort numTables = reader.ReadUShort();
            reader.ReadUShort(); // searchRange
            reader.ReadUShort(); // entrySelector
            reader.ReadUShort(); // rangeShift

            for (var i = 0; i < numTables; i++)
            {
                string tag = Encoding.ASCII.GetString(reader.ReadBytes(4));
                reader.ReadUInt32();           // checksum (not validated)
                uint offset = reader.ReadUInt32();
                uint length = reader.ReadUInt32();
                _directory[tag] = (offset, length);
            }

            OutlineKind = _directory.ContainsKey("CFF ") ? SfntOutlineKind.Cff
                : _directory.ContainsKey("glyf") ? SfntOutlineKind.TrueType
                : SfntOutlineKind.Unknown;
        }

        public bool HasTable(string tag) => _directory.ContainsKey(tag);

        public IEnumerable<string> TableTags => _directory.Keys;

        /// <summary>Raw bytes of a table (exact length), or null if the font lacks it.</summary>
        public byte[]? GetTableBytes(string tag)
        {
            if (!_directory.TryGetValue(tag, out (uint Offset, uint Length) entry)) return null;
            // Defend against a directory entry that points outside the buffer.
            if (entry.Offset > (uint)_data.Length || entry.Offset + entry.Length > (uint)_data.Length)
                return null;
            var result = new byte[entry.Length];
            Array.Copy(_data, entry.Offset, result, 0, entry.Length);
            return result;
        }

        public ushort UnitsPerEm => Head?.UnitsPerEm ?? 0;

        public int NumGlyphs => Maxp?.NumGlyphs ?? 0;

        // ---- lazy, cached typed accessors ----

        private bool _headTried;
        private HeadTable? _head;
        public HeadTable? Head => GetCached(ref _headTried, ref _head, "head", b => new HeadTable(b));

        private bool _maxpTried;
        private MaxPTable? _maxp;
        public MaxPTable? Maxp => GetCached(ref _maxpTried, ref _maxp, "maxp", b => new MaxPTable(b));

        private bool _hheaTried;
        private HheaTable? _hhea;
        public HheaTable? Hhea => GetCached(ref _hheaTried, ref _hhea, "hhea", b => new HheaTable(b));

        private bool _cmapTried;
        private CmapTable? _cmap;
        public CmapTable? Cmap => GetCached(ref _cmapTried, ref _cmap, "cmap", b => new CmapTable(b));

        private bool _nameTried;
        private NameTable? _name;
        public NameTable? Name => GetCached(ref _nameTried, ref _name, "name", b => new NameTable(b));

        private bool _postTried;
        private PostTable? _post;
        public PostTable? Post => GetCached(ref _postTried, ref _post, "post", b => new PostTable(b));

        private bool _cffTried;
        private Type1Table? _cff;
        public Type1Table? Cff => GetCached(ref _cffTried, ref _cff, "CFF ", b => new Type1Table(b));

        private bool _hmtxTried;
        private HmtxTable? _hmtx;
        public HmtxTable? Hmtx
        {
            get
            {
                if (_hmtxTried) return _hmtx;
                _hmtxTried = true;
                byte[]? bytes = GetTableBytes("hmtx");
                HheaTable? hhea = Hhea;
                MaxPTable? maxp = Maxp;
                if (bytes is null || hhea is null || maxp is null) return _hmtx;
                var table = new HmtxTable(bytes);
                table.Process(hhea.NumberOfHMetrics, maxp.NumGlyphs);
                _hmtx = table;
                return _hmtx;
            }
        }

        private bool _locaTried;
        private LocaTable? _loca;
        public LocaTable? Loca
        {
            get
            {
                if (_locaTried) return _loca;
                _locaTried = true;
                byte[]? bytes = GetTableBytes("loca");
                HeadTable? head = Head;
                MaxPTable? maxp = Maxp;
                if (bytes is null || head is null || maxp is null) return _loca;
                var table = new LocaTable(bytes);
                table.Process(maxp.NumGlyphs, head.IndexToLocFormat == IndexToLocFormat.Offset16);
                _loca = table;
                return _loca;
            }
        }

        private bool _glyfTried;
        private GlyphTable? _glyf;
        public GlyphTable? Glyf
        {
            get
            {
                if (_glyfTried) return _glyf;
                _glyfTried = true;
                byte[]? bytes = GetTableBytes("glyf");
                LocaTable? loca = Loca;
                MaxPTable? maxp = Maxp;
                if (bytes is null || loca is null || maxp is null) return _glyf;
                var table = new GlyphTable(bytes);
                table.Process(maxp.NumGlyphs, loca);
                _glyf = table;
                return _glyf;
            }
        }

        private T? GetCached<T>(ref bool tried, ref T? cache, string tag, Func<byte[], T> ctor)
            where T : class
        {
            if (tried) return cache;
            tried = true;
            byte[]? bytes = GetTableBytes(tag);
            if (bytes is not null) cache = ctor(bytes);
            return cache;
        }
    }
}
