using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Optional
{
    public class VmtxTable : IFontTable
    {
        public static string Tag => "vmtx";

        public List<VerticalMetricsEntry> VerticalMetrics { get; } = new List<VerticalMetricsEntry>();

        public short[]? TopSideBearings { get; private set; }

        private readonly BigEndianReader _reader;

        public VmtxTable(byte[] data)
        {
            _reader = new BigEndianReader(data);
        }

        // numOfLongVerMetrics from vhea table
        public void Process(ushort numOfLongVerMetrics)
        {
            for (var i = 0; i < numOfLongVerMetrics; i++)
            {
                VerticalMetrics.Add(new VerticalMetricsEntry(_reader.ReadBytes(4)));
            }

            if (_reader.WordsRemaining <= 0) return;
            TopSideBearings = new short[_reader.WordsRemaining];
            for (var i = 0; i < _reader.WordsRemaining; i++)
            {
                TopSideBearings[i] = _reader.ReadShort();
            }
        }
    }
}