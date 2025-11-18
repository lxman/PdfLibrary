using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.TtTables
{
    public class GaspTable : IFontTable
    {
        public static string Tag => "gasp";

        public ushort Version { get; set; }

        public List<GaspRange> GaspRanges { get; set; } = new List<GaspRange>();

        public GaspTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            Version = reader.ReadUShort();
            ushort numRanges = reader.ReadUShort();

            for (var i = 0; i < numRanges; i++)
            {
                var range = new GaspRange
                {
                    RangeMaxPPEM = reader.ReadUShort(),
                    RangeGaspBehavior = (RangeGaspBehavior)reader.ReadUShort()
                };

                GaspRanges.Add(range);
            }
        }
    }
}