using System;
using System.Collections.Generic;
using System.Linq;
using FontParser.Reader;
using FontParser.Tables.Cff.Type1;
using FontParser.Tables.Cff.Type2.FontDictSelect;
using FontParser.Tables.Common.ItemVariationStore;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace FontParser.Tables.Cff.Type2
{
    public class Type2Table : IFontTable
    {
        public static string Tag => "CFF2";

        public List<List<string>> CharStringList { get; } = new List<List<string>>();

        public Type2Header Header { get; private set; }

        public List<List<byte>> GlobalSubroutines { get; private set; }

        public List<CffDictEntry> TopDictOperatorEntries { get; } = new List<CffDictEntry>();

        public List<CffDictEntry> PrivateDictOperatorEntries { get; } = new List<CffDictEntry>();

        private readonly Type2TopDictOperatorEntries _type2TopDictOperatorEntries =
            new Type2TopDictOperatorEntries(new Dictionary<ushort, CffDictEntry?>());

        private readonly Type2FontDictEntries _type2FontDictEntries =
            new Type2FontDictEntries(new Dictionary<ushort, CffDictEntry?>());

        private readonly BigEndianReader _reader;

        private readonly PrivateDictOperatorEntries _privateDictOperatorEntries =
            new PrivateDictOperatorEntries(new Dictionary<ushort, CffDictEntry?>());

        public Type2Table(byte[] data)
        {
            _reader = new BigEndianReader(data);
        }

        public void Process(ushort numGlyphs)
        {
            Header = new Type2Header(_reader);

            _reader.Seek(Header.HeaderSize);

            ReadTopDictEntries(_reader, Header.TopDictSize);

            _reader.Seek(Header.HeaderSize + Header.TopDictSize);

            var gsIndex = new Type2Index(_reader);
            GlobalSubroutines = gsIndex.Data;

            _reader.Seek(Convert.ToInt64(TopDictOperatorEntries.First(e => e.Name == "CharStringIndexOffset").Operand));
            var csIndex = new Type2Index(_reader);

            CffDictEntry? entry = TopDictOperatorEntries.FirstOrDefault(e => e.Name == "FontDictSelectOffset");
            IFdSelect? fdSelect = null;
            if (!(entry is null))
            {
                _reader.Seek(Convert.ToInt64(TopDictOperatorEntries.First(e => e.Name == "FontDictSelectOffset").Operand));
                fdSelect = _reader.PeekBytes(1)[0] switch
                {
                    0 => new FdsFormat0(_reader, numGlyphs),
                    3 => new FdsFormat3(_reader),
                    4 => new FdsFormat4(_reader),
                    _ => fdSelect
                };
            }

            ItemVariationStore? itemVariationStore = null;
            long? vsOffset =
                Convert.ToInt64(TopDictOperatorEntries.FirstOrDefault(e => e.Name == "VariationStoreOffset")?.Operand ?? -1);
            if (vsOffset > 0)
            {
                // Why do I have to add 2 to make it work?
                _reader.Seek(vsOffset.Value + 2);
                itemVariationStore = new ItemVariationStore(_reader);
            }

            _reader.Seek(Convert.ToInt64(TopDictOperatorEntries.First(e => e.Name == "FontDictIndexOffset").Operand));
            var fdIndex = new Type2Index(_reader);
            var fontDicts = new List<CffDictEntry>();
            fdIndex.Data.ForEach(bytes =>
            {
                var dict = new List<CffDictEntry>();
                DictEntryReader.Read(bytes,
                    _type2FontDictEntries,
                    dict);
                fontDicts.AddRange(dict);
            });
            var privateDicts = new List<List<CffDictEntry>>();
            var charStringData = new List<CharStringData>();
            fontDicts.ForEach(cffDictEntry =>
            {
                if (!(cffDictEntry.Operand is List<double> data)) return;
                double size = data[0];
                double offset = data[1];
                _reader.Seek(Convert.ToInt64(offset));
                var dict = new List<CffDictEntry>();
                ReadPrivateDictEntries(
                    _reader,
                    size,
                    dict,
                    itemVariationStore?.ItemVariationData[0].RegionIndexes ?? new List<ushort>(),
                    charStringData);
                privateDicts.Add(dict);
            });

            // TODO: Come back when CFF2 is more stable
            //var csdIndex = 0;
            //csIndex.Data.ForEach(d =>
            //{
            //    CharStringData csData = SelectCharStringData(charStringData, csdIndex++);
            //    var charStringParser = new CharStringParser(
            //        48,
            //        d,
            //        GlobalSubroutines,
            //        csData.Subroutines ?? new List<List<byte>>(),
            //        csData.NominalWidthX ?? 0
            //    );
            //    CharStringList.Add(charStringParser.Parse());
            //});
        }

        private CharStringData SelectCharStringData(List<CharStringData> data, int index)
        {
            if (data.Count == 1) return data[0];
            throw new NotImplementedException("Multiple private dictionaries not implemented yet.");
        }

        private void ReadTopDictEntries(BigEndianReader reader, ushort size)
        {
            List<byte> bytes = reader.ReadBytes(Convert.ToInt32(size)).ToList();
            DictEntryReader.Read(bytes, _type2TopDictOperatorEntries, TopDictOperatorEntries);
        }

        private void ReadPrivateDictEntries(
            BigEndianReader reader,
            double size,
            List<CffDictEntry> entries,
            List<ushort> activeVariationRegionData,
            List<CharStringData> csData)
        {
            long pdStart = reader.Position;
            List<byte> bytes = reader.ReadBytes(Convert.ToInt32(size)).ToList();
            DictEntryReader.Read(bytes, _privateDictOperatorEntries, entries, activeVariationRegionData);
            List<List<byte>>? localSubroutines = ReadLocalSubroutines(entries, pdStart);
            int? nominalWidthX = Convert.ToInt32(entries.FirstOrDefault(e => e.Name == "nominalWidthX")?.Operand ?? 0);
            csData.Add(new CharStringData(localSubroutines, nominalWidthX));
        }

        private List<List<byte>>? ReadLocalSubroutines(List<CffDictEntry> privateDict, long offset)
        {
            var localSubroutines = new List<List<byte>>();
            CffDictEntry? subrEntry = privateDict.FirstOrDefault(e => e.Name == "Subrs");
            if (subrEntry is null) return null;
            _reader.Seek(offset + Convert.ToInt64(subrEntry.Operand));
            ushort localSubrCount = _reader.ReadUShort();
            if (localSubrCount == 0) return null;
            byte offSize = _reader.ReadByte();
            List<uint> localSubrOffsets = _reader.ReadOffsets(offSize, localSubrCount + 1u).ToList();
            var subrIndex = 0;
            while (subrIndex < localSubrOffsets.Count - 1)
            {
                localSubroutines.Add(new List<byte>(_reader.ReadBytes(localSubrOffsets[subrIndex + 1] - localSubrOffsets[subrIndex])));
                subrIndex++;
            }
            return localSubroutines;
        }
    }
}