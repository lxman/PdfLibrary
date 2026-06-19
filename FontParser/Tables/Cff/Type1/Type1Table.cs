using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FontParser.Extensions;
using FontParser.Reader;
using FontParser.Tables.Cff.Type1.Charsets;
using FontParser.Tables.Cff.Type1.FdSelect;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS8601 // Possible null reference assignment.

namespace FontParser.Tables.Cff.Type1
{
    public class Type1Table : IFontTable
    {
        public static string Tag => "CFF ";

        public IEncoding Encoding { get; }

        public ICharset CharSet { get; }

        public List<string> Names { get; } = new List<string>();

        public List<string> Strings { get; } = new List<string>();

        public List<List<string>> CharStringList { get; } = new List<List<string>>();

        /// <summary>
        /// Raw charstring data for each glyph (for direct parsing)
        /// </summary>
        public List<List<byte>> RawCharStrings { get; private set; } = new List<List<byte>>();

        /// <summary>
        /// Global subroutines for charstring parsing
        /// </summary>
        public List<List<byte>> GlobalSubroutines { get; private set; } = new List<List<byte>>();

        /// <summary>
        /// Local subroutines for charstring parsing
        /// </summary>
        public List<List<byte>> LocalSubroutines => _localSubroutines;

        /// <summary>
        /// Raw bytes of the single Top DICT entry, captured before SID resolution. The CFF subsetter
        /// copies non-offset operators from here verbatim (preserving original SIDs) and replaces only
        /// the offset operators, so no SID re-encoding is needed.
        /// </summary>
        public byte[] RawTopDict { get; private set; } = Array.Empty<byte>();

        /// <summary>
        /// Raw bytes of the (non-CID) Private DICT, for verbatim re-emit during subsetting.
        /// </summary>
        public byte[] RawPrivateDict { get; private set; } = Array.Empty<byte>();

        /// <summary>
        /// Raw bytes of a CUSTOM charset (Top DICT charset offset &gt; 2), for verbatim re-emit. Empty
        /// when the font uses a predefined charset (0/1/2), which the subsetter copies as a literal.
        /// </summary>
        public byte[] RawCharset { get; private set; } = Array.Empty<byte>();

        /// <summary>
        /// True for a CID-keyed CFF (the Top DICT carries a ROS operator with FDArray/FDSelect).
        /// </summary>
        public bool IsCid { get; private set; }

        /// <summary>Raw Name INDEX entries, for byte-exact verbatim re-emit (the decoded
        /// <see cref="Names"/> strings are lossy for bytes &gt;= 0x80).</summary>
        public List<List<byte>> RawNameIndex { get; private set; } = new List<List<byte>>();

        /// <summary>Raw String INDEX entries, for byte-exact verbatim re-emit (the decoded
        /// <see cref="Strings"/> are lossy for bytes &gt;= 0x80; SID-&gt;string consistency requires
        /// the original bytes since charset is kept verbatim).</summary>
        public List<List<byte>> RawStringIndex { get; private set; } = new List<List<byte>>();

        /// <summary>Per-FD raw data for a CID-keyed CFF (FDArray order); empty for non-CID. Used by the
        /// subsetter to re-emit the FDArray + per-FD Private DICTs + local subrs.</summary>
        public IReadOnlyList<CffCidFd> CidFds { get; private set; } = new List<CffCidFd>();

        /// <summary>Raw bytes of the FDSelect table (CID only), kept verbatim by the subsetter.</summary>
        public byte[] RawFdSelect { get; private set; } = Array.Empty<byte>();

        /// <summary>
        /// Nominal width for glyph width calculations
        /// </summary>
        public int NominalWidthX { get; private set; }

        /// <summary>
        /// Font matrix from CFF Top DICT
        /// </summary>
        public List<double>? FontMatrix
        {
            get
            {
                CffDictEntry? entry = _topDictOperatorEntries.FirstOrDefault(e => e.Name == "FontMatrix");
                return entry?.Operand as List<double>;
            }
        }

        private readonly Type1TopDictOperatorEntries _type1TopDictOperatorEntries =
            new Type1TopDictOperatorEntries(new Dictionary<ushort, CffDictEntry?>());

        private readonly PrivateDictOperatorEntries _privateDictOperatorEntries =
            new PrivateDictOperatorEntries(new Dictionary<ushort, CffDictEntry?>());

        private readonly List<CffDictEntry> _topDictOperatorEntries = new List<CffDictEntry>();
        private readonly List<CffDictEntry> _type1PrivateDictOperatorEntries = new List<CffDictEntry>();
        private readonly List<CidFontDictEntry> _type1FontDictOperatorEntries = new List<CidFontDictEntry>();
        private readonly List<List<byte>> _localSubroutines = new List<List<byte>>();

        /// <summary>
        /// For CID-keyed CFF: maps glyph index → the font-dict entry whose private dict / local
        /// subroutines apply to that glyph. Null for non-CID fonts.
        /// </summary>
        private Dictionary<ushort, NameDictEntry>? _cidFdSelect;

        public Type1Table(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            var header = new Type1Header(reader);

            var nameIndex = new Type1Index(reader);
            RawNameIndex = nameIndex.Data;

            foreach (List<byte> bytes in nameIndex.Data)
            {
                Names.Add(System.Text.Encoding.ASCII.GetString(bytes.ToArray()));
            }

            var topDictIndex = new Type1Index(reader);

            ReadTopDictEntries(topDictIndex.Data);
            RawTopDict = topDictIndex.Data.Count > 0 ? topDictIndex.Data[0].ToArray() : Array.Empty<byte>();

            var stringIndex = new Type1Index(reader);
            RawStringIndex = stringIndex.Data;

            foreach (List<byte> bytes in stringIndex.Data)
            {
                Strings.Add(System.Text.Encoding.ASCII.GetString(bytes.ToArray()));
            }

            ResolveDictSids(_topDictOperatorEntries);

            var globalSubrIndex = new Type1Index(reader);
            List<List<byte>> globalSubroutines = globalSubrIndex.Data;

            byte encodingFormat = reader.ReadByte();
            Encoding = encodingFormat switch
            {
                0 => new Encoding0(reader),
                1 => new Encoding1(reader),
                _ => Encoding
            };

            reader.Seek(Convert.ToInt64(_topDictOperatorEntries.First(e => e.Name == "CharStrings").Operand));

            var charStrings = new Type1Index(reader);

            long charsetOffset = Convert.ToInt64(_topDictOperatorEntries.First(e => e.Name == "charset").Operand);
            reader.Seek(charsetOffset);
            long charsetStart = reader.Position;

            byte charsetFormat = reader.ReadByte();
            CharSet = charsetFormat switch
            {
                0 => new CharsetsFormat0(reader,
                    Convert.ToUInt16(charStrings.Data.Count)),
                1 => new CharsetsFormat1(reader,
                    Convert.ToUInt16(charStrings.Data.Count)),
                2 => new CharsetsFormat2(reader,
                    Convert.ToUInt16(charStrings.Data.Count)),
                _ => CharSet
            };
            // Custom charset (offset > 2) is kept verbatim by the subsetter; predefined (0/1/2) is no table.
            RawCharset = charsetOffset > 2
                ? data[(int)charsetStart..(int)reader.Position]
                : Array.Empty<byte>();
            IsCid = _topDictOperatorEntries.Any(e => e.Name == "ROS");
            if (IsCid)
            {
                ProcessCid(data, reader, charStrings, globalSubroutines);
                return;
            }
            var privateDictInfo = (List<double>?)_topDictOperatorEntries.FirstOrDefault(e => e.Name == "Private")?.Operand;
            if (privateDictInfo is null) return;
            reader.Seek(Convert.ToInt64(privateDictInfo[1]));
            double privateDictSize = privateDictInfo[0];
            ReadPrivateDictEntries(reader, privateDictSize);
            BuildLocalSubroutines(reader, privateDictInfo);
            BuildCharStrings(charStrings, globalSubroutines);
        }

        /// <summary>
        /// Get glyph outline for a specific glyph index
        /// </summary>
        /// <param name="glyphIndex">Index of the glyph (0-based)</param>
        /// <returns>GlyphOutline with path commands, or null if invalid index</returns>
        public GlyphOutline? GetGlyphOutline(int glyphIndex) => GetGlyphOutline(glyphIndex, true);

        private GlyphOutline? GetGlyphOutline(int glyphIndex, bool allowSeac)
        {
            if (glyphIndex < 0 || glyphIndex >= RawCharStrings.Count)
                return null;

            // For CID-keyed CFF, each glyph is assigned to one of multiple font dicts via the
            // FDSelect table; subroutines and nominalWidthX are per-FD, not table-wide.
            List<List<byte>> localSubrs = _localSubroutines;
            int nominalWidth = NominalWidthX;
            if (_cidFdSelect is not null &&
                _cidFdSelect.TryGetValue((ushort)glyphIndex, out NameDictEntry? fd))
            {
                localSubrs = fd.LocalSubroutines;
                object? nwxOp = fd.Private.Find(e => e.Name == "nominalWidthX")?.Operand;
                nominalWidth = nwxOp is null ? 0 : Convert.ToInt32(nwxOp);
            }

            var parser = new CharStringParser(
                48,
                RawCharStrings[glyphIndex],
                GlobalSubroutines,
                localSubrs,
                nominalWidth
            );

            GlyphOutline outline = parser.ParseToOutline();

            // Deprecated seac: the glyph is a composite of a base + accent glyph, both referenced
            // by StandardEncoding code, with the accent shifted by (adx, ady). Compose them.
            // One level only (allowSeac=false on the recursive lookups) to avoid seac cycles.
            if (allowSeac && parser.Seac is { } seac)
            {
                GlyphOutline? composed = ComposeSeac(seac, outline.Width);
                if (composed is not null) return composed;
            }

            return outline;
        }

        private GlyphOutline? ComposeSeac((float Adx, float Ady, int Bchar, int Achar) seac, float? width)
        {
            string? baseName = StandardEncoding.GetName(seac.Bchar);
            string? accentName = StandardEncoding.GetName(seac.Achar);
            if (baseName is null || accentName is null) return null;

            int baseGid = GetGlyphIndexByName(baseName);
            int accentGid = GetGlyphIndexByName(accentName);
            if (baseGid <= 0 || accentGid <= 0) return null;

            GlyphOutline? baseOutline = GetGlyphOutline(baseGid, false);
            GlyphOutline? accentOutline = GetGlyphOutline(accentGid, false);
            if (baseOutline is null || accentOutline is null) return null;

            var composed = new GlyphOutline { Width = width };
            composed.Commands.AddRange(baseOutline.Commands);
            foreach (PathCommand cmd in accentOutline.Commands)
            {
                composed.Commands.Add(Translate(cmd, seac.Adx, seac.Ady));
            }

            composed.MinX = Math.Min(baseOutline.MinX, accentOutline.MinX + seac.Adx);
            composed.MinY = Math.Min(baseOutline.MinY, accentOutline.MinY + seac.Ady);
            composed.MaxX = Math.Max(baseOutline.MaxX, accentOutline.MaxX + seac.Adx);
            composed.MaxY = Math.Max(baseOutline.MaxY, accentOutline.MaxY + seac.Ady);
            return composed;
        }

        private static PathCommand Translate(PathCommand cmd, float dx, float dy)
        {
            switch (cmd)
            {
                case MoveToCommand m: return new MoveToCommand(m.Point.X + dx, m.Point.Y + dy);
                case LineToCommand l: return new LineToCommand(l.Point.X + dx, l.Point.Y + dy);
                case CubicBezierCommand c:
                    return new CubicBezierCommand(
                        c.Control1.X + dx, c.Control1.Y + dy,
                        c.Control2.X + dx, c.Control2.Y + dy,
                        c.EndPoint.X + dx, c.EndPoint.Y + dy);
                default: return cmd; // ClosePathCommand has no coordinates
            }
        }

        private Dictionary<string, int>? _nameToGid;

        /// <summary>Resolves a glyph name to its GID via the charset, or -1 if not present.</summary>
        public int GetGlyphIndexByName(string name)
        {
            if (_nameToGid is null) BuildNameToGid();
            return _nameToGid!.TryGetValue(name, out int gid) ? gid : -1;
        }

        private void BuildNameToGid()
        {
            _nameToGid = new Dictionary<string, int> { [".notdef"] = 0 };

            void Add(int gid, ushort sid)
            {
                string n = ResolveSid(sid);
                if (!string.IsNullOrEmpty(n) && !_nameToGid!.ContainsKey(n)) _nameToGid![n] = gid;
            }

            switch (CharSet)
            {
                case CharsetsFormat0 f0:
                    for (var i = 0; i < f0.Glyphs.Count; i++) Add(i + 1, f0.Glyphs[i]);
                    break;
                case CharsetsFormat1 f1:
                {
                    var gid = 1;
                    foreach (var r in f1.Ranges)
                        for (var k = 0; k <= r.NumberLeft; k++) Add(gid++, (ushort)(r.First + k));
                    break;
                }
                case CharsetsFormat2 f2:
                {
                    var gid = 1;
                    foreach (var r in f2.Ranges)
                        for (var k = 0; k <= r.NumberLeft; k++) Add(gid++, (ushort)(r.First + k));
                    break;
                }
            }
        }

        // Standard CFF
        // One set of local subroutines for the entire font
        private void BuildCharStrings(Type1Index charStrings, List<List<byte>> globalSubroutines)
        {
            // Store raw data for later glyph outline extraction
            RawCharStrings = charStrings.Data;
            GlobalSubroutines = globalSubroutines;
            NominalWidthX = Convert.ToInt32(_type1PrivateDictOperatorEntries.FirstOrDefault(e => e.Name == "nominalWidthX")?.Operand ?? 0);

            foreach (
                CharStringParser parser in
                charStrings
                    .Data
                    .Select(bytes =>
                        new CharStringParser(
                            48,
                            bytes,
                            globalSubroutines,
                            _localSubroutines,
                            NominalWidthX
                        )
                    )
            )
            {
                CharStringList.Add(parser.Parse());
            }
        }

        // CID font
        // Individual set of local subroutines for each font
        private void BuildCharStringCid(
            List<byte> charString,
            List<List<byte>> globalSubroutines,
            NameDictEntry entry)
        {
            var parser = new CharStringParser(
                48,
                charString,
                globalSubroutines,
                entry.LocalSubroutines,
                Convert.ToInt32(entry.Private.FirstOrDefault(e => e.Name == "nominalWidthX")?.Operand ?? 0)
            );
            CharStringList.Add(parser.Parse());
        }

        private void ProcessCid(byte[] data, BigEndianReader reader, Type1Index charStrings, List<List<byte>> globalSubroutines)
        {
            // Make raw CharStrings + global subroutines available to GetGlyphOutline. Without
            // this the non-CID code path treats CID fonts as having zero glyphs.
            RawCharStrings = charStrings.Data;
            GlobalSubroutines = globalSubroutines;

            reader.Seek(Convert.ToInt64(_topDictOperatorEntries.First(e => e.Name == "FDArray").Operand));
            var fdArrayIndex = new Type1Index(reader);
            ReadFontDictEntries(fdArrayIndex.Data);
            var fontDictEntries = new List<NameDictEntry>();
            var cidFds = new List<CffCidFd>();

            for (var fd = 0; fd < _type1FontDictOperatorEntries.Count; fd++)
            {
                CidFontDictEntry oe = _type1FontDictOperatorEntries[fd];
                CffDictEntry privateDictEntry = oe.Entries.First(e => e.Name == "Private");
                CffDictEntry fontNameEntry = oe.Entries.First(e => e.Name == "FontName");
                List<double> entries = (List<double>?)privateDictEntry.Operand
                                       ?? throw new InvalidDataException("Private dict entry has no operand");
                reader.Seek(Convert.ToInt64(entries[1]));
                long pdStart = reader.Position;
                ReadPrivateDictEntries(reader, entries[0]); // sets RawPrivateDict to this FD's bytes
                byte[] rawPrivate = RawPrivateDict;
                var ndEntry = new NameDictEntry(ResolveSid(Convert.ToUInt16(fontNameEntry.Operand)),
                    _type1PrivateDictOperatorEntries.Clone());
                if (ndEntry.Private.Find(p => p.Name == "Subrs") is { } pd)
                {
                    reader.Seek(pdStart + Convert.ToInt64(pd.Operand));
                    ushort localSubrCount = reader.ReadUShort();
                    if (localSubrCount > 0) // 0 local subrs is valid — keep the FD, just no subr INDEX
                    {
                        byte offSize = reader.ReadByte();
                        List<uint> localSubrOffsets = reader.ReadOffsets(offSize, localSubrCount + 1u).ToList();
                        for (var subrIndex = 0; subrIndex < localSubrOffsets.Count - 1; subrIndex++)
                            ndEntry.LocalSubroutines.Add(new List<byte>(reader.ReadBytes(localSubrOffsets[subrIndex + 1] - localSubrOffsets[subrIndex])));
                    }
                }
                fontDictEntries.Add(ndEntry);
                cidFds.Add(new CffCidFd(fdArrayIndex.Data[fd].ToArray(), rawPrivate, ndEntry.LocalSubroutines));
                _type1PrivateDictOperatorEntries.Clear();
            }
            CidFds = cidFds;

            long fdSelectOffset = Convert.ToInt64(_topDictOperatorEntries.Find(e => e.Name == "FDSelect").Operand);
            reader.Seek(fdSelectOffset);
            Dictionary<ushort, NameDictEntry> fdSelectEntries =
                ReadFdSelectEntries(reader, fontDictEntries, charStrings.Data.Count);
            RawFdSelect = data[(int)fdSelectOffset..(int)reader.Position];
            _cidFdSelect = fdSelectEntries;
            for (ushort x = 0; x < fdSelectEntries.Count; x++)
            {
                BuildCharStringCid(charStrings.Data[x], globalSubroutines, fdSelectEntries[x]);
            }
        }

        private void ReadTopDictEntries(List<List<byte>> data)
        {
            foreach (List<byte> bytes in data)
            {
                DictEntryReader.Read(bytes, _type1TopDictOperatorEntries, _topDictOperatorEntries);
            }
        }

        private static Dictionary<ushort, NameDictEntry> ReadFdSelectEntries(
            BigEndianReader reader, List<NameDictEntry> fontDictEntries, int numGlyphs)
        {
            var fdSelect = new Dictionary<ushort, NameDictEntry>();
            byte fdSelectFormat = reader.ReadByte();
            switch (fdSelectFormat)
            {
                case 0:
                    // Format 0: one byte per glyph giving the FD index. CFF spec §19.
                    byte[] selects = reader.ReadBytes(numGlyphs);
                    for (ushort i = 0; i < selects.Length; i++)
                    {
                        byte fdIdx0 = selects[i];
                        if (fdIdx0 >= fontDictEntries.Count) continue;
                        fdSelect.Add(i, fontDictEntries[fdIdx0]);
                    }
                    break;
                case 3:
                    // Format 3 (CFF spec §19): nRanges ranges + a Sentinel. Range r covers glyphs
                    // [Ranges[r].First, next) where next = Ranges[r+1].First, or Sentinel for the last
                    // range. A single-range FDSelect is valid and common (single-FD CID fonts) — the
                    // previous code assumed >= 2 ranges and threw on Ranges[1].
                    var ranges = new FdsFormat3(reader);
                    for (var r = 0; r < ranges.Ranges.Count; r++)
                    {
                        int first = ranges.Ranges[r].First;
                        int next = r + 1 < ranges.Ranges.Count ? ranges.Ranges[r + 1].First : ranges.Sentinel;
                        byte fd = ranges.Ranges[r].FontDictIndex;
                        if (fd >= fontDictEntries.Count) continue; // defensive: malformed FD index
                        for (int gid = first; gid < next && gid < numGlyphs; gid++)
                            fdSelect[(ushort)gid] = fontDictEntries[fd];
                    }
                    break;
            }
            return fdSelect;
        }

        private void ReadFontDictEntries(List<List<byte>> data)
        {
            foreach (List<byte> unit in data)
            {
                var cidFdEntry = new CidFontDictEntry();
                DictEntryReader.Read(unit, _type1TopDictOperatorEntries, cidFdEntry.Entries);
                _type1FontDictOperatorEntries.Add(cidFdEntry);
            }
        }

        private void ReadPrivateDictEntries(BigEndianReader reader, double size)
        {
            List<byte> bytes = reader.ReadBytes(Convert.ToInt32(size)).ToList();
            RawPrivateDict = bytes.ToArray();
            DictEntryReader.Read(bytes, _privateDictOperatorEntries, _type1PrivateDictOperatorEntries);
        }

        private void ResolveDictSids(List<CffDictEntry> entries)
        {
            entries.ForEach(e =>
            {
                if (e.OperandKind != OperandKind.StringId && e.OperandKind != OperandKind.SidSidNumber) return;
                if (e.OperandKind == OperandKind.SidSidNumber)
                {
                    var operands = (List<double>)e.Operand;
                    e.Operand = new SidSidSid();
                    if (operands[0] is var sid1)
                    {
                        (e.Operand as SidSidSid)!.Sid1 = ResolveSid(Convert.ToUInt16(sid1));
                    }
                    if (operands[1] is var sid2)
                    {
                        (e.Operand as SidSidSid)!.Sid2 = ResolveSid(Convert.ToUInt16(sid2));
                    }
                    if (operands[2] is var sid3)
                    {
                        (e.Operand as SidSidSid)!.Sid3 = ResolveSid(Convert.ToUInt16(sid3));
                    }
                }
                else
                {
                    var sid = Convert.ToUInt16(e.Operand);
                    e.Operand = ResolveSid(sid);
                }
            });
        }

        private string ResolveSid(ushort sid)
        {
            if (sid <= StandardStrings.StandardStringsLimit)
            {
                return StandardStrings.GetString(sid) ?? string.Empty;
            }

            return Strings[sid - StandardStrings.StandardStringsLimit - 1];
        }

        private void BuildLocalSubroutines(BigEndianReader reader, List<double> privateDictInfo)
        {
            CffDictEntry? subrEntry = _type1PrivateDictOperatorEntries.FirstOrDefault(e => e.Name == "Subrs");
            if (subrEntry is null) return;
            reader.Seek(Convert.ToInt64(privateDictInfo[1]) + Convert.ToInt64(subrEntry.Operand));
            ushort localSubrCount = reader.ReadUShort();
            if (localSubrCount == 0) return;
            byte offSize = reader.ReadByte();
            List<uint> localSubrOffsets = reader.ReadOffsets(offSize, localSubrCount + 1u).ToList();
            var subrIndex = 0;
            while (subrIndex < localSubrOffsets.Count - 1)
            {
                _localSubroutines.Add(new List<byte>(reader.ReadBytes(localSubrOffsets[subrIndex + 1] - localSubrOffsets[subrIndex])));
                subrIndex++;
            }
        }
    }
}