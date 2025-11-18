using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Morx
{
    public class MetamorphosisSubTable
    {
        public ApplyDirection Direction { get; private set; }

        public ProcessDirection ProcessDirection { get; private set; }

        public ProcessLogicalOrder LogicalProcessOrder { get; private set; }

        public MetamorphosisSubTable(BigEndianReader reader)
        {
            uint length = reader.ReadUInt32();
            uint coverage = reader.ReadUInt32();
            var subFeatureFlags = new List<uint>();
            for (var i = 0; i < length - 12; i++)
            {
                subFeatureFlags.Add(reader.ReadUInt32());
            }
            ProcessCoverageFlags(coverage);
        }

        private void ProcessCoverageFlags(uint flags)
        {
            if ((flags & 0x20000000) != 0)
            {
                Direction = ApplyDirection.BothHorizontalAndVertical;
            }
            else
            {
                Direction = (ApplyDirection)(flags & 0x80000000);
            }

            ProcessDirection = (ProcessDirection)(flags & 0x40000000);
            if ((flags & 0x10000000) != 0)
            {
                LogicalProcessOrder = Direction == ApplyDirection.Horizontal
                    ? ProcessLogicalOrder.Forward
                    : ProcessLogicalOrder.Reverse;
            }
            else
            {
                LogicalProcessOrder = ProcessLogicalOrder.NA;
            }
        }
    }
}