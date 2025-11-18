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

        public List<CmapEncoding> Encodings { get; } = new List<CmapEncoding>();

        public List<EncodingRecord> EncodingRecords { get; } = new List<EncodingRecord>();

        public List<ICmapSubtable> SubTables { get; } = new List<ICmapSubtable>();

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
                            try
                            {
                                subTable = new CmapSubtablesFormat14(reader);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Invalid data for encoding record - not read.");
                            }
                        }
                        break;
                }

                if (!(subTable is null))
                {
                    SubTables.Add(subTable);
                    Encodings.Add(new CmapEncoding(encodingRecord, subTable));
                }
            }
        }

        public ushort GetGlyphId(ushort codePoint)
        {
            return SubTables
                .Select(subTable => subTable.GetGlyphId(codePoint))
                .FirstOrDefault(glyphId => glyphId != 0);
        }
    }
}