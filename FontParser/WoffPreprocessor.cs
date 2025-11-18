using System;
using System.Collections.Generic;
using FontParser.Models;
using FontParser.Reader;
using FontParser.Tables.Woff;
using FontParser.Tables.Woff.Zlib;
using FontParser.Tables.WOFF2;
using FontParser.Tables.WOFF2.Brotli;

namespace FontParser
{
    public class WoffPreprocessor
    {
        public GlyfTransform GlyfTransform { get; private set; }

        public LocaTransform LocaTransform { get; private set; }

        public HmtxTransform HmtxTransform { get; private set; }

        public List<TableRecord> TableRecords { get; } = new List<TableRecord>();

        private readonly FileByteReader _reader;

        public WoffPreprocessor(FileByteReader reader, int version)
        {
            _reader = reader;
            var flavor = (Flavor)reader.ReadUInt32();
            uint length = reader.ReadUInt32();
            ushort numTables = reader.ReadUInt16();
            ushort reserved = reader.ReadUInt16();
            uint totalSfntSize = reader.ReadUInt32();
            uint totalCompressedSize = version == 2 ? reader.ReadUInt32() : 0;
            ushort majorVersion = reader.ReadUInt16();
            ushort minorVersion = reader.ReadUInt16();
            uint metaOffset = reader.ReadUInt32();
            uint metaLength = reader.ReadUInt32();
            uint metaOriginalLength = reader.ReadUInt32();
            uint privateOffset = reader.ReadUInt32();
            uint privateLength = reader.ReadUInt32();
            var directoryEntries = new List<IDirectoryEntry>();
            for (var i = 0; i < numTables; i++)
            {
                switch (version)
                {
                    case 1:
                        directoryEntries.Add(new WoffTableDirectoryEntry(reader));
                        break;

                    case 2:
                        directoryEntries.Add(new Woff2TableDirectoryEntry(reader));
                        break;
                }
            }

            long woff2OffsetTracker = 0;
            long woff2ExpectedStart = 0;
            byte[] uncompressedWoff2Data = Array.Empty<byte>();
            if (version == 2) uncompressedWoff2Data = BrotliUtility.Decompress(reader.ReadBytes(totalCompressedSize));
            directoryEntries.ForEach(d =>
            {
                var tag = string.Empty;
                byte[] uncompressedWoffData = Array.Empty<byte>();
                switch (d)
                {
                    case WoffTableDirectoryEntry entry:
                        _reader.Seek(entry.Offset);
                        byte[] compressedData = _reader.ReadBytes(entry.CompressedLength);
                        int cmf = compressedData[0];
                        int flags = compressedData[1];
                        int compressionMethod = cmf & 0x0F;
                        int compressionInfo = (cmf & 0xF0) >> 4;
                        int checkBits = flags & 0x0F;
                        int dictionary = (flags & 0x20) >> 5;
                        int compressionLevel = (flags & 0xC0) >> 6;
                        uncompressedWoffData = entry.CompressedLength != entry.OriginalLength
                            ? ZlibUtility.Inflate(dictionary == 0
                                ? compressedData[2..]
                                : compressedData[6..])
                            : compressedData;
                        tag = entry.Tag;
                        break;

                    case Woff2TableDirectoryEntry entry:
                        tag = entry.Tag;
                        long dataStart = woff2OffsetTracker;
                        if (woff2ExpectedStart != dataStart)
                        {
                            throw new ApplicationException("Bad WOFF2 file");
                        }
                        woff2OffsetTracker += entry.TransformLength is null ? entry.OriginalLength : Convert.ToUInt32(entry.TransformLength);
                        long dataLength = woff2OffsetTracker - dataStart;
                        woff2ExpectedStart = dataStart + dataLength;
                        if (tag != "glyf" && tag != "loca" && tag != "hmtx")
                        {
                            uncompressedWoffData = uncompressedWoff2Data[(int)dataStart..(int)(dataStart + dataLength)];
                        }
                        else
                        {
                            switch (tag)
                            {
                                case "glyf":
                                    GlyfTransform = (GlyfTransform)entry.Transformation;
                                    uncompressedWoffData = uncompressedWoff2Data[(int)dataStart..(int)(dataStart + dataLength)];
                                    break;

                                case "loca":
                                    LocaTransform = (LocaTransform)entry.Transformation;
                                    uncompressedWoffData = uncompressedWoff2Data[(int)dataStart..(int)(dataStart + dataLength)];
                                    break;

                                case "hmtx":
                                    HmtxTransform = (HmtxTransform)entry.Transformation;
                                    uncompressedWoffData = uncompressedWoff2Data[(int)dataStart..(int)(dataStart + dataLength)];
                                    break;
                            }
                        }
                        break;
                }

                if (tag != string.Empty)
                {
                    TableRecords.Add(new TableRecord { Tag = tag, Data = uncompressedWoffData });
                }
            });
        }
    }
}