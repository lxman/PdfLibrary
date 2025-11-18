using System;
using System.Collections.Generic;
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

        private readonly Type1TopDictOperatorEntries _type1TopDictOperatorEntries =
            new Type1TopDictOperatorEntries(new Dictionary<ushort, CffDictEntry?>());

        private readonly PrivateDictOperatorEntries _privateDictOperatorEntries =
            new PrivateDictOperatorEntries(new Dictionary<ushort, CffDictEntry?>());

        private readonly List<CffDictEntry> _topDictOperatorEntries = new List<CffDictEntry>();
        private readonly List<CffDictEntry> _type1PrivateDictOperatorEntries = new List<CffDictEntry>();
        private readonly List<CidFontDictEntry> _type1FontDictOperatorEntries = new List<CidFontDictEntry>();
        private readonly List<List<byte>> _localSubroutines = new List<List<byte>>();

        public Type1Table(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            var header = new Type1Header(reader);

            var nameIndex = new Type1Index(reader);

            foreach (List<byte> bytes in nameIndex.Data)
            {
                Names.Add(System.Text.Encoding.ASCII.GetString(bytes.ToArray()));
            }

            var topDictIndex = new Type1Index(reader);

            ReadTopDictEntries(topDictIndex.Data);

            var stringIndex = new Type1Index(reader);

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

            reader.Seek(Convert.ToInt64(_topDictOperatorEntries.First(e => e.Name == "charset").Operand));

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
            bool isCid = _topDictOperatorEntries.Any(e => e.Name == "ROS");
            if (isCid)
            {
                ProcessCid(reader, charStrings, globalSubroutines);
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

        // Standard CFF
        // One set of local subroutines for the entire font
        private void BuildCharStrings(Type1Index charStrings, List<List<byte>> globalSubroutines)
        {
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
                            Convert.ToInt32(_type1PrivateDictOperatorEntries.FirstOrDefault(e => e.Name == "nominalWidthX")?.Operand ?? 0)
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

        private void ProcessCid(BigEndianReader reader, Type1Index charStrings, List<List<byte>> globalSubroutines)
        {
            reader.Seek(Convert.ToInt64(_topDictOperatorEntries.First(e => e.Name == "FDArray").Operand));
            var fdArrayIndex = new Type1Index(reader);
            ReadFontDictEntries(fdArrayIndex.Data);
            var fontDictEntries = new List<NameDictEntry>();
            _type1FontDictOperatorEntries.ForEach(oe =>
            {
                CffDictEntry privateDictEntry = oe.Entries.First(e => e.Name == "Private");
                CffDictEntry fontNameEntry = oe.Entries.First(e => e.Name == "FontName");
                var entries = (List<double>?)privateDictEntry.Operand;
                reader.Seek(Convert.ToInt64(entries[1]));
                long pdStart = reader.Position;
                ReadPrivateDictEntries(reader, entries[0]);
                var ndEntry = new NameDictEntry(ResolveSid(Convert.ToUInt16(fontNameEntry.Operand)),
                    _type1PrivateDictOperatorEntries.Clone());
                if (ndEntry.Private.Find(p => p.Name == "Subrs") is { } pd)
                {
                    reader.Seek(pdStart + Convert.ToInt64(pd.Operand));
                    ushort localSubrCount = reader.ReadUShort();
                    if (localSubrCount == 0) return;
                    byte offSize = reader.ReadByte();
                    List<uint> localSubrOffsets = reader.ReadOffsets(offSize, localSubrCount + 1u).ToList();
                    var subrIndex = 0;
                    while (subrIndex < localSubrOffsets.Count - 1)
                    {
                        ndEntry.LocalSubroutines.Add(new List<byte>(reader.ReadBytes(localSubrOffsets[subrIndex + 1] - localSubrOffsets[subrIndex])));
                        subrIndex++;
                    }
                }
                fontDictEntries.Add(ndEntry);
                _type1PrivateDictOperatorEntries.Clear();
            });
            reader.Seek(Convert.ToInt64(_topDictOperatorEntries.Find(e => e.Name == "FDSelect").Operand));
            Dictionary<ushort, NameDictEntry> fdSelectEntries = ReadFdSelectEntries(reader, fontDictEntries);
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

        private static Dictionary<ushort, NameDictEntry> ReadFdSelectEntries(BigEndianReader reader, List<NameDictEntry> fontDictEntries)
        {
            var fdSelect = new Dictionary<ushort, NameDictEntry>();
            byte fdSelectFormat = reader.ReadByte();
            switch (fdSelectFormat)
            {
                case 0:
                    byte[] selects = reader.ReadBytes(fontDictEntries.Count + 1);
                    ushort index = 0;
                    foreach (byte select in selects)
                    {
                        fdSelect.Add(index++, fontDictEntries[select]);
                    }
                    break;
                case 3:
                    var ranges = new FdsFormat3(reader);
                    ushort rangeIndex = 0;
                    byte fdIndex = ranges.Ranges[0].FontDictIndex;
                    Range3 nextRange = ranges.Ranges[1];
                    ushort nextTerminator = nextRange.First;
                    ushort currentIndex = 0;
                    while (true)
                    {
                        while (currentIndex < nextTerminator)
                        {
                            fdSelect.Add(currentIndex, fontDictEntries[fdIndex]);
                            currentIndex++;
                        }
                        fdIndex = nextRange.FontDictIndex;
                        rangeIndex++;
                        if (rangeIndex >= ranges.Ranges.Count)
                        {
                            break;
                        }
                        if (rangeIndex == ranges.Ranges.Count - 1)
                        {
                            nextTerminator = ranges.Sentinel;
                        }
                        else
                        {
                            nextRange = ranges.Ranges[rangeIndex + 1];
                            nextTerminator = nextRange.First;
                        }
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