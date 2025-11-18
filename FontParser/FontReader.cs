using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FontParser.Extensions;
using FontParser.Models;
using FontParser.Reader;
using FontParser.Tables;
using FontParser.Tables.Hhea;
using FontParser.Tables.Hmtx;
using FontParser.Tables.Name;
using FontParser.Tables.TtTables.Glyf;
using FontParser.Tables.WOFF2;
using FontParser.Tables.WOFF2.GlyfReconstruct;

namespace FontParser
{
    public class FontReader
    {
        public async Task<List<FontStructure>> ReadFileAsync(string file)
        {
            var result = new List<FontStructure>();

            if (!File.Exists(file))
            {
                return result;
            }

            await using FileStream fs = File.OpenRead(file);
            var reader = new FileByteReader(fs);

            if (file.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase))
            {
                return await Task.Run(() => ParseTtc(reader, file));
            }
            else if (file.EndsWith(".woff", StringComparison.OrdinalIgnoreCase))
            {
                FontStructure font = ParseWoff(reader, file);
                result.Add(font);
            }
            else if (file.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase))
            {
                FontStructure font = ParseWoff2(reader, file);
                result.Add(font);
            }
            else
            {
                FontStructure font = ParseSingleParallel(reader, new FontStructure(file));
                result.Add(font);
            }

            return result;
        }

        public async Task<List<(string, List<IFontTable>)>?> GetTablesAsync(string file)
        {
            List<FontStructure> fontStructures = await ReadFileAsync(file);
            return CompileTableDictionary(fontStructures);
        }

        public List<FontStructure> ReadFile(string file)
        {
            var reader = new FileByteReader(file);
            var fontStructure = new FontStructure(file);
            byte[] data = reader.ReadBytes(4);

            if (EqualByteArrays(data, new byte[] { 0x00, 0x01, 0x00, 0x00 }))
            {
                fontStructure.FileType = FileType.Ttf;
            }
            else if (EqualByteArrays(data, new byte[] { 0x4F, 0x54, 0x54, 0x4F }))
            {
                fontStructure.FileType = FileType.Otf;
            }
            else if (EqualByteArrays(data, new byte[] { 0x74, 0x74, 0x63, 0x66 }))
            {
                fontStructure.FileType = FileType.Ttc;
            }
            else if (EqualByteArrays(data, new byte[] { 0x4F, 0x54, 0x43, 0x46 }))
            {
                fontStructure.FileType = FileType.Otc;
            }
            else if (EqualByteArrays(data, new byte[] { 0x77, 0x4F, 0x46, 0x46 }))
            {
                fontStructure.FileType = FileType.Woff;
            }
            else if (EqualByteArrays(data, new byte[] { 0x77, 0x4F, 0x46, 0x32 }))
            {
                fontStructure.FileType = FileType.Woff2;
            }
            else
            {
                throw new InvalidDataException("We do not know how to parse this file.");
            }

            switch (fontStructure.FileType)
            {
                case FileType.Unk:
                    Console.WriteLine("This is an unknown file type.");
                    break;

                case FileType.Ttf:
                case FileType.Otf:
                    return new List<FontStructure> { ParseSingleParallel(reader, fontStructure) };

                case FileType.Ttc:
                    return ParseTtc(reader, file);

                case FileType.Otc:
                    Console.WriteLine("I am not aware how to parse otc files yet.");
                    break;

                case FileType.Woff:
                    return new List<FontStructure> { ParseWoff(reader, file) };

                case FileType.Woff2:
                    return new List<FontStructure> { ParseWoff2(reader, file) };
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            return new List<FontStructure>();
        }

        public List<(string, List<IFontTable>)>? GetTables(string file)
        {
            List<FontStructure> fontStructure = ReadFile(file);
            return CompileTableDictionary(fontStructure);
        }

        public List<(string, List<string>)> GetTableNames(string file)
        {
            var toReturn = new List<(string, List<string>)>();
            List<(string, List<IFontTable>)> tables = GetTables(file);
            tables.ForEach(t =>
            {
                toReturn.Add((t.Item1,
                    t.Item2.Select(i => i.GetType().GetProperty("Tag").GetValue(i).ToString()).ToList()));
            });
            return toReturn;
        }

        private static List<(string, List<IFontTable>)> CompileTableDictionary(List<FontStructure> fontStructures)
        {
            var toReturn = new List<(string, List<IFontTable>)>();
            fontStructures.ForEach(fs =>
            {
                var nameTable = (NameTable?)fs.Tables.FirstOrDefault(t => t is NameTable);
                NameRecord? nameRecord =
                    nameTable?.NameRecords.FirstOrDefault(r =>
                        r.LanguageId.Contains("English") && r.NameId == "Full Name");
                if (nameRecord is null)
                {
                    return;
                }

                string name = nameRecord.Name ?? string.Empty;
                var toAdd = new List<IFontTable>();
                fs.Tables.ForEach(t => { toAdd.Add(t); });
                toReturn.Add((name, toAdd));
            });

            return toReturn;
        }

        private static List<FontStructure> ParseTtc(FileByteReader reader, string file)
        {
            var fontStructures = new List<FontStructure>();
            ushort majorVersion = reader.ReadUInt16();
            ushort minorVersion = reader.ReadUInt16();
            uint numFonts = reader.ReadUInt32();
            var offsets = new uint[numFonts];
            for (var i = 0; i < numFonts; i++)
            {
                offsets[i] = reader.ReadUInt32();
            }

            if (majorVersion == 2)
            {
                uint dsigTag = reader.ReadUInt32();
                uint dsigLength = reader.ReadUInt32();
                uint dsigOffset = reader.ReadUInt32();
            }

            for (var i = 0; i < numFonts; i++)
            {
                reader.Seek(offsets[i]);
                var fontStructure = new FontStructure(file) { FileType = FileType.Ttc };
                _ = reader.ReadBytes(4);
                Console.WriteLine($"\tParsing subfont {i + 1}");
                fontStructures.Add(ParseSingleParallel(reader, fontStructure));
            }

            return fontStructures;
        }

        private static FontStructure ParseWoff(FileByteReader reader, string file)
        {
            var fontStructure = new FontStructure(file);
            var woffProcessor = new WoffPreprocessor(reader, 1);
            fontStructure.TableRecords = woffProcessor.TableRecords;
            fontStructure.CollectTableNames();
            fontStructure.ProcessParallel();
            return fontStructure;
        }

        private static FontStructure ParseWoff2(FileByteReader reader, string file)
        {
            var fontStructure = new FontStructure(file);
            var woffProcessor = new WoffPreprocessor(reader, 2);
            fontStructure.TableRecords = woffProcessor.TableRecords;
            fontStructure.CollectTableNames();
            fontStructure.ProcessParallel(
                woffProcessor.GlyfTransform == GlyfTransform.Transform,
                true,
                woffProcessor.HmtxTransform == HmtxTransform.Transform);

            if (woffProcessor.GlyfTransform == GlyfTransform.Transform)
            {
                byte[]? glyphData = woffProcessor.TableRecords.FirstOrDefault(t => t.Tag == "glyf")?.Data;
                if (glyphData is null)
                {
                    throw new InvalidDataException("No glyph data found in WOFF2 file.");
                }

                var glyphTableData = new TransformedGlyfTable(new BigEndianReader(glyphData));
                GlyphTable glyphTable = ReconstructGlyfTable(glyphTableData);
                fontStructure.Tables.Add(glyphTable);
            }
            if (woffProcessor.HmtxTransform == HmtxTransform.Transform)
            {
                HheaTable? hheaTable = fontStructure.Tables.OfType<HheaTable>().FirstOrDefault();
                MaxPTable? maxpTable = fontStructure.Tables.OfType<MaxPTable>().FirstOrDefault();
                if (hheaTable is null || maxpTable is null)
                {
                    throw new InvalidDataException("No hhea or maxp table found in WOFF2 file.");
                }
                byte[]? hmtxData = woffProcessor.TableRecords.FirstOrDefault(t => t.Tag == "hmtx")?.Data;
                if (hmtxData is null)
                {
                    throw new InvalidDataException("No hmtx data found in WOFF2 file.");
                }
                var hmtxTableData = new TransformedHmtxTable(
                    new BigEndianReader(hmtxData),
                    hheaTable.NumberOfHMetrics,
                    maxpTable.NumGlyphs);
                HmtxTable hmtxTable = ReconstructHmtxTable(hmtxTableData);
                fontStructure.Tables.Add(hmtxTable);
            }
            return fontStructure;
        }

        private static FontStructure ParseSingleParallel(FileByteReader reader, FontStructure fontStructure)
        {
            fontStructure.TableCount = reader.ReadUInt16();
            fontStructure.SearchRange = reader.ReadUInt16();
            fontStructure.EntrySelector = reader.ReadUInt16();
            fontStructure.RangeShift = reader.ReadUInt16();
            for (var i = 0; i < fontStructure.TableCount; i++)
            {
                fontStructure.TableRecords.Add(ReadTableRecord(reader));
            }

            fontStructure.TableRecords = fontStructure.TableRecords.OrderBy(x => x.Offset).ToList();
            fontStructure.CollectTableNames();
            fontStructure.TableRecords.ForEach(x =>
            {
                reader.Seek(x.Offset);
                List<byte> sectionData = reader.ReadBytes(x.Length).ToList();
                // Pad 4 bytes for badly formed tables
                if (reader.BytesRemaining >= 4) sectionData.AddRange(reader.ReadBytes(4));
                x.Data = sectionData.ToArray();
            });
            fontStructure.ProcessParallel();
            return fontStructure;
        }

        private static bool EqualByteArrays(byte[]? a, byte[]? b)
        {
            if (a is null || b is null)
            {
                return a is null && b is null;
            }

            return a.Length == b.Length && a.SequenceEqual(b);
        }

        private static TableRecord ReadTableRecord(FileByteReader reader)
        {
            return new TableRecord
            {
                Tag = reader.ReadString(4),
                CheckSum = reader.ReadUInt32(),
                Offset = reader.ReadUInt32(),
                Length = reader.ReadUInt32()
            };
        }

        private static GlyphTable ReconstructGlyfTable(TransformedGlyfTable table)
        {
            var pointTransformer = new PointTransform();
            var locaOffsets = new List<uint>();
            var nContourReader = new TReader<short>(table.NContourStream);
            var nPointsReader = new TReader<ushort>(table.NPointsStream);
            var flagReader = new TReader<byte>(table.FlagStream);
            var glyphReader = new TReader<byte>(table.GlyphStream);
            var compositeReader = new TReader<byte>(table.CompositeStream);
            var bboxBitmapReader = new BitmapReader(table.BBoxBitmapStream);
            var bboxDataReader = new TReader<short>(table.BBoxStream);
            var instructionReader = new TReader<byte>(table.InstructionStream);
            var overlapSimpleBitmapReader = new BitmapReader(table.OverlapSimpleBitmap);
            var glyphs = new List<List<IGlyphInfo>>();
            var glyphCount = 0;
            while (glyphCount < table.GlyphCount)
            {
                short numberOfContours = nContourReader.Read();
                var glyphInfos = new List<IGlyphInfo>();
                if (numberOfContours > 0)
                {
                    // Simple glyph
                    List<ushort> nPoints = nPointsReader.Read(numberOfContours).ToList();
                    List<ushort> endpointsOfContours = CalcEndpoints(nPoints);
                    int pointCount = nPoints.Sum(x => x);
                    var currentPoint = new Point(0, 0);
                    var glyphInfo = new SimpleGlyphInfo();
                    List<byte> flags = flagReader.Read(pointCount).ToList();
                    var coordinates = new List<SimpleGlyphCoordinate>();
                    flags.ForEach(f =>
                    {
                        bool onCurve = (f & 0x80) == 0;
                        int byteCount = f & 0x7F;
                        byte[] data;
                        if (byteCount < 0x54)
                        {
                            data = new[] { glyphReader.Read() };
                        }
                        else if (byteCount < 0x78)
                        {
                            data = glyphReader.Read(2);
                        }
                        else if (byteCount < 0x7C)
                        {
                            data = glyphReader.Read(3);
                        }
                        else
                        {
                            data = glyphReader.Read(4);
                        }

                        Point? p = pointTransformer.Transform(byteCount, data);
                        if (p is null)
                        {
                            throw new InvalidDataException("Point transformation failed.");
                        }

                        currentPoint = currentPoint.Add(p.Value);

                        coordinates.Add(new SimpleGlyphCoordinate(currentPoint, onCurve));
                    });
                    Rectangle? boundingBox = ReadBoundingBox(bboxBitmapReader, bboxDataReader) ??
                                             CalcBoundingBox(coordinates);
                    glyphInfo.Coordinates.AddRange(coordinates);
                    glyphInfo.XMin = Convert.ToInt16(boundingBox.Value.X);
                    glyphInfo.YMin = Convert.ToInt16(boundingBox.Value.Y);
                    glyphInfo.XMax = Convert.ToInt16(boundingBox.Value.X + boundingBox.Value.Width);
                    glyphInfo.YMax = Convert.ToInt16(boundingBox.Value.Y + boundingBox.Value.Height);
                    glyphInfo.EndPointsOfContours.AddRange(endpointsOfContours);
                    glyphInfo.InstructionCount = glyphReader.Read255Uint16();
                    glyphInfos.Add(glyphInfo);
                }
                else if (numberOfContours < 0)
                {
                    // Composite glyph
                    var cgi = new CompositeGlyphInfo();
                    var flag = (CompositeGlyphFlags)BinaryPrimitives.ReadUInt16BigEndian(compositeReader.Read(2));
                    var cycle = true;
                    while (cycle)
                    {
                        cgi.Elements.Add(BuildCompositeGlyphElement(compositeReader, flag));
                        if (flag.HasFlag(CompositeGlyphFlags.MoreComponents))
                        {
                            flag = (CompositeGlyphFlags)BinaryPrimitives.ReadUInt16BigEndian(compositeReader.Read(2));
                        }
                        else
                        {
                            cycle = false;
                        }
                    }

                    if (cgi.Elements.Any(e => e.Flags.HasFlag(CompositeGlyphFlags.WeHaveInstructions)))
                    {
                        cgi.InstructionCount = glyphReader.Read255Uint16();
                    }

                    Rectangle? boundingBox = ReadBoundingBox(bboxBitmapReader, bboxDataReader);
                    if (!(boundingBox is null))
                    {
                        cgi.XMin = Convert.ToInt16(boundingBox.Value.X);
                        cgi.YMin = Convert.ToInt16(boundingBox.Value.Y);
                        cgi.XMax = Convert.ToInt16(boundingBox.Value.X + boundingBox.Value.Width);
                        cgi.YMax = Convert.ToInt16(boundingBox.Value.Y + boundingBox.Value.Height);
                    }

                    glyphInfos.Add(cgi);
                }
                else
                {
                    // Empty glyph
                    locaOffsets.Add(locaOffsets.Count == 0
                        ? 0
                        : locaOffsets[^1]);
                }

                glyphCount++;
                glyphs.Add(glyphInfos);
            }

            glyphs.ForEach(g =>
            {
                g.ForEach(gi =>
                {
                    if (gi.InstructionCount > 0)
                    {
                        gi.Instructions.AddRange(instructionReader.Read(gi.InstructionCount));
                    }
                });
            });

            var glyphTable = new GlyphTable(Array.Empty<byte>());
            glyphTable.Woff2Reconstruct(glyphs);
            return glyphTable;
        }

        private static HmtxTable ReconstructHmtxTable(TransformedHmtxTable table)
        {
            var toReturn = new HmtxTable(Array.Empty<byte>());
            var index = 0;
            foreach (ushort advanceWidth in table.AdvanceWidth)
            {
                var longHMetricRecord = new LongHMetricRecord(
                    new BigEndianReader(Array.Empty<byte>()),
                    advanceWidth,
                    !(table.Lsb is null) ? table.Lsb[index] : (short)0);
                toReturn.LongHMetricRecords.Add(longHMetricRecord);
                index++;
            }

            if (table.LeftSideBearing is null) return toReturn;
            foreach (short lsb in table.LeftSideBearing)
            {
                toReturn.LeftSideBearings.Add(lsb);
            }
            return toReturn;
        }

        private static List<ushort> CalcEndpoints(List<ushort> nPoints)
        {
            ushort sum = 0;
            var endpoints = new List<ushort>();
            foreach (ushort n in nPoints)
            {
                sum += n;
                endpoints.Add(Convert.ToUInt16(sum - 1));
            }

            return endpoints;
        }

        private static CompositeGlyphElement BuildCompositeGlyphElement(TReader<byte> compositeReader,
            CompositeGlyphFlags flag)
        {
            var info = new CompositeGlyphElement()
            {
                Flags = flag,
                GlyphIndex = BinaryPrimitives.ReadUInt16BigEndian(compositeReader.Read(2))
            };
            int bytesToRead = info.Flags.HasFlag(CompositeGlyphFlags.Arg1And2AreWords)
                ? 2
                : 1;
            bool readSigned = info.Flags.HasFlag(CompositeGlyphFlags.ArgsAreXyValues);
            info.Arg1 = readSigned
                ? ConvertShortToInt(compositeReader.Read(bytesToRead))
                : ConvertUShortToInt(compositeReader.Read(bytesToRead));
            info.Arg2 = readSigned
                ? ConvertShortToInt(compositeReader.Read(bytesToRead))
                : ConvertUShortToInt(compositeReader.Read(bytesToRead));
            int transformCount = info.Flags.HasFlag(CompositeGlyphFlags.WeHaveAScale)
                ? 1
                : info.Flags.HasFlag(CompositeGlyphFlags.WeHaveAnXAndYScale)
                    ? 2
                    : info.Flags.HasFlag(CompositeGlyphFlags.WeHaveATwoByTwo)
                        ? 4
                        : 0;
            for (var i = 0; i < transformCount; i++)
            {
                info.TransformData[i] = BinaryPrimitives.ReadUInt16BigEndian(compositeReader.Read(2)).ToF2Dot14();
            }

            return info;
        }

        private static Rectangle CalcBoundingBox(List<SimpleGlyphCoordinate> coordinates)
        {
            if (coordinates.Count == 0) return new Rectangle(0, 0, 0, 0);
            int xMin = coordinates.Min(c => Convert.ToInt32(c.Point.X));
            int yMin = coordinates.Min(c => Convert.ToInt32(c.Point.Y));
            int xMax = coordinates.Max(c => Convert.ToInt32(c.Point.X));
            int yMax = coordinates.Max(c => Convert.ToInt32(c.Point.Y));
            return new Rectangle(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        private static Rectangle? ReadBoundingBox(BitmapReader bboxBitmapReader, TReader<short> bboxDataReader)
        {
            if (!bboxBitmapReader.Read()) return null;
            short xMin = bboxDataReader.Read();
            short yMin = bboxDataReader.Read();
            short xMax = bboxDataReader.Read();
            short yMax = bboxDataReader.Read();
            return new Rectangle(xMax, yMin, xMax - xMin, yMax - yMin);
        }

        private static int ConvertShortToInt(byte[] bytes)
        {
            return bytes.Length == 1
                ? (sbyte)bytes[0]
                : BinaryPrimitives.ReadInt16BigEndian(bytes);
        }

        private static int ConvertUShortToInt(byte[] bytes)
        {
            return bytes.Length == 1
                ? bytes[0]
                : BinaryPrimitives.ReadUInt16BigEndian(bytes);
        }
    }
}