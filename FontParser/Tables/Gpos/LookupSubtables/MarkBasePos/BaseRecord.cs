using FontParser.Reader;

namespace FontParser.Tables.Gpos.LookupSubtables.MarkBasePos
{
    public class BaseRecord
    {
        public ushort[] BaseAnchorOffsets { get; }

        public BaseRecord(ushort markClassCount, BigEndianReader reader)
        {
            BaseAnchorOffsets = new ushort[markClassCount];
            for (var i = 0; i < markClassCount; i++)
            {
                BaseAnchorOffsets[i] = reader.ReadUShort();
            }
        }
    }
}