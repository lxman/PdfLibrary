using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using FontParser.Reader;
using FontParser.Tables.Cmap.SubTables;

namespace FontParser.Tables.Cmap
{
    public class CmapTable : IFontTable
    {
        public static string Tag => "cmap";

        public ushort Version { get; }

        public List<CmapEncoding> Encodings { get; } = new();

        public List<EncodingRecord> EncodingRecords { get; } = new();

        public List<ICmapSubtable> SubTables { get; } = new();

        public CmapTable(byte[] cmapData)
        {
            var reader = new BigEndianReader(cmapData);
            byte[] data = reader.ReadBytes(2);
            Version = BinaryPrimitives.ReadUInt16BigEndian(data);
            data = reader.ReadBytes(2);
            ushort numTables = BinaryPrimitives.ReadUInt16BigEndian(data);
            for (var i = 0; i < numTables; i++)
            {
                EncodingRecords.Add(new EncodingRecord(reader.ReadBytes(EncodingRecord.RecordSize)));
            }
            EncodingRecords = EncodingRecords.OrderBy(x => x.Offset).ToList();
            foreach (EncodingRecord? encodingRecord in EncodingRecords)
            {
                try
                {
                    // Check if we have enough data left to read even the format field (2 bytes)
                    if (encodingRecord.Offset + 2 > cmapData.Length)
                    {
                        // Skip this encoding record - offset points beyond available data
                        continue;
                    }

                    reader.Seek(encodingRecord.Offset);
                    data = reader.PeekBytes(2);
                    ushort format = BinaryPrimitives.ReadUInt16BigEndian(data);
                    ICmapSubtable? subTable = null;
                    switch (format)
                    {
                        case 0:
                            subTable = new CmapSubtableFormat0(reader);
                            break;

                        case 2:
                            subTable = new CmapSubtablesFormat2(reader);
                            break;

                        case 4:
                            subTable = new CmapSubtablesFormat4(reader);
                            break;

                        case 6:
                            subTable = new CmapSubtablesFormat6(reader);
                            break;

                        case 8:
                            subTable = new CmapSubtablesFormat8(reader);
                            break;

                        case 10:
                            subTable = new CmapSubtablesFormat10(reader);
                            break;

                        case 12:
                            subTable = new CmapSubtablesFormat12(reader);
                            break;

                        case 13:
                            subTable = new CmapSubtablesFormat13(reader);
                            break;

                        case 14:
                            if (encodingRecord is { PlatformId: 0, UnicodeEncoding: { } } && (int)encodingRecord.UnicodeEncoding.Value == 5)
                            {
                                subTable = new CmapSubtablesFormat14(reader);
                            }
                            break;
                    }

                    if (!(subTable is null))
                    {
                        SubTables.Add(subTable);
                        Encodings.Add(new CmapEncoding(encodingRecord, subTable));
                    }
                }
                catch (Exception)
                {
                    // If parsing this encoding record fails (malformed/truncated data), skip it and continue
                    // This commonly happens with subset fonts that have corrupted cmap tables
                }
            }
        }

        /// <summary>
        /// Maps a code point to a glyph, consulting subtables in PLATFORM-PREFERENCE order rather than
        /// the order they happen to appear in the file.
        ///
        /// <para>Encoding records are sorted by file offset in the constructor, which bears no relation
        /// to how trustworthy a subtable is for a Unicode lookup. Taking the first non-zero hit in that
        /// order meant a font listing Mac/Roman at a lower offset than Windows/UnicodeBmp resolved
        /// Unicode code points through MacRoman — identical below U+0080, where the two encodings
        /// coincide, and wrong above it. Real case: C059-Italic.otf orders its subtables Mac/Roman,
        /// Unicode, Windows/UnicodeBmp, so U+00E4 'a-dieresis' returned the glyph MacRoman keeps at
        /// 0xE4. Fonts that happened to list Unicode first were correct only by luck.</para>
        ///
        /// <para>Every subtable is still tried, so a font carrying only a Mac subtable resolves exactly
        /// as before; only the ORDER changes, and only where two subtables disagree.</para>
        /// </summary>
        public ushort GetGlyphId(ushort codePoint)
        {
            foreach (CmapEncoding encoding in Encodings.OrderBy(SubtableRank))
            {
                ushort glyphId = encoding.SubTable.GetGlyphId(codePoint);
                if (glyphId != 0) return glyphId;
            }

            return 0;
        }

        /// <summary>Lower ranks are consulted first. OrderBy is a stable sort, so subtables sharing a
        /// rank keep their existing file-offset order.</summary>
        private static int SubtableRank(CmapEncoding encoding)
        {
            EncodingRecord record = encoding.Encoding;
            switch (record.PlatformId)
            {
                case PlatformId.Windows when record.WindowsEncoding == WindowsEncodingId.UnicodeUCS4:
                    return 0;
                case PlatformId.Windows when record.WindowsEncoding == WindowsEncodingId.UnicodeBmp:
                    return 1;
                case PlatformId.Unicode:
                    return 2;
                default:
                    // Mac/Roman, (3,0) Symbol, ISO and anything else: usable as a fallback, never as
                    // the first answer to a Unicode question.
                    return 3;
            }
        }
    }
}