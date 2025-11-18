using System;
using System.Collections.Generic;
using FontParser.Reader;
using Common_TupleVariationStore_Tuple = FontParser.Tables.Common.TupleVariationStore.Tuple;

namespace FontParser.Tables.Gvar
{
    public class GvarTable : IFontTable
    {
        public static string Tag => "gvar";

        public Header Header { get; }

        public List<Common_TupleVariationStore_Tuple> Tuples { get; } = new List<Common_TupleVariationStore_Tuple>();

        public List<Common.TupleVariationStore.Header> GlyphVariations { get; } = new List<Common.TupleVariationStore.Header>();

        public GvarTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            Header = new Header(reader);
            var glyphVariationDataOffsets = new List<uint>();
            bool readLongOffsets = (Header.Flags & 0x0001) != 0;
            for (var i = 0; i <= Header.GlyphCount; i++)
            {
                glyphVariationDataOffsets.Add(readLongOffsets ? reader.ReadUInt32() : Convert.ToUInt32(reader.ReadUShort() * 2));
            }
            reader.Seek(Header.SharedTuplesOffset);
            for (var i = 0; i < Header.SharedTupleCount; i++)
            {
                Tuples.Add(new Common_TupleVariationStore_Tuple(reader, Header.AxisCount));
            }

            for (var i = 0; i < glyphVariationDataOffsets.Count - 1; i++)
            {
                reader.Seek(Header.GlyphVariationDataArrayOffset + glyphVariationDataOffsets[i]);
                GlyphVariations.Add(new Common.TupleVariationStore.Header(reader, Header.AxisCount, false));
            }
        }
    }
}