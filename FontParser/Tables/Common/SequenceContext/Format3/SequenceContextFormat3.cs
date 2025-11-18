using System.Collections.Generic;
using System.Linq;
using FontParser.Reader;
using FontParser.Tables.Common.CoverageFormat;
using FontParser.Tables.Common.SequenceContext.Format1;

#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.

namespace FontParser.Tables.Common.SequenceContext.Format3
{
    public class SequenceContextFormat3 : ILookupSubTable, ISequenceContext
    {
        public List<SequenceLookup> SequenceLookups { get; } = new List<SequenceLookup>();

        public List<ICoverageFormat> CoverageFormats { get; } = new List<ICoverageFormat>();

        public SequenceContextFormat3(BigEndianReader reader)
        {
            long startOfTable = reader.Position;
            _ = reader.ReadUShort();
            ushort glyphCount = reader.ReadUShort();
            ushort seqLookupCount = reader.ReadUShort();
            List<ushort> coverageOffsets = reader.ReadUShortArray(glyphCount).ToList();
            for (var i = 0; i < seqLookupCount; i++)
            {
                SequenceLookups.Add(new SequenceLookup(reader.ReadBytes(4)));
            }

            long before = reader.Position;
            coverageOffsets.ForEach(o =>
            {
                reader.Seek(startOfTable + o);
                byte format = reader.PeekBytes(2)[1];
                ICoverageFormat coverage = format switch
                {
                    1 => new CoverageFormat1(reader),
                    2 => new CoverageFormat2(reader),
                    _ => null
                };
                if (!(coverage is null)) CoverageFormats.Add(coverage);
            });
            reader.Seek(before);
        }
    }
}