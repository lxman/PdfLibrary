using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Vdmx
{
    public class VdmxTable : IFontTable
    {
        public static string Tag => "VDMX";

        public ushort Version { get; }

        public List<RatioRange> RatioRanges { get; } = new List<RatioRange>();

        public List<VdmxGroup> Groups { get; } = new List<VdmxGroup>();

        public VdmxTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            Version = reader.ReadUShort();
            ushort numRecs = reader.ReadUShort();
            ushort numRatios = reader.ReadUShort();

            for (var i = 0; i < numRatios; i++)
            {
                RatioRanges.Add(new RatioRange(reader));
            }

            var groupOffsets = new ushort[numRecs];
            for (var i = 0; i < numRecs; i++)
            {
                groupOffsets[i] = reader.ReadUShort();
            }

            for (var i = 0; i < numRecs; i++)
            {
                Groups.Add(new VdmxGroup(data[groupOffsets[i]..]));
            }
        }
    }
}