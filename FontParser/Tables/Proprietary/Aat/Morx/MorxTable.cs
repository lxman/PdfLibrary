using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Morx
{
    public class MorxTable : IFontTable
    {
        public static string Tag => "morx";

        public Header Header { get; }

        public List<ChainTable> Chains { get; } = new List<ChainTable>();

        public MorxTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            Header = new Header(reader);
            for (var i = 0; i < Header.NChains; i++)
            {
                Chains.Add(new ChainTable(reader));
            }
        }
    }
}