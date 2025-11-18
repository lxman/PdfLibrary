using FontParser.Reader;

namespace FontParser.Tables.Common
{
    public class ValueRecord
    {
        public short? XPlacement { get; internal set; }

        public short? YPlacement { get; internal set; }

        public short? XAdvance { get; internal set; }

        public short? YAdvance { get; internal set; }

        public ushort? XPlaDeviceOffset { get; internal set; }

        public ushort? YPlaDeviceLength { get; internal set; }

        public ushort? XAdvDeviceOffset { get; internal set; }

        public ushort? YAdvDeviceLength { get; internal set; }

        public ValueRecord(ValueFormat flags, BigEndianReader reader)
        {
            if (flags.HasFlag(ValueFormat.XPlacement))
            {
                XPlacement = reader.ReadShort();
            }
            if (flags.HasFlag(ValueFormat.YPlacement))
            {
                YPlacement = reader.ReadShort();
            }
            if (flags.HasFlag(ValueFormat.XAdvance))
            {
                XAdvance = reader.ReadShort();
            }

            if (flags.HasFlag(ValueFormat.YAdvance))
            {
                YAdvance = reader.ReadShort();
            }
            if (flags.HasFlag(ValueFormat.XPlacementDevice))
            {
                XPlaDeviceOffset = reader.ReadUShort();
            }

            if (flags.HasFlag(ValueFormat.YPlacementDevice))
            {
                YPlaDeviceLength = reader.ReadUShort();
            }

            if (flags.HasFlag(ValueFormat.XAdvanceDevice))
            {
                XAdvDeviceOffset = reader.ReadUShort();
            }

            if (flags.HasFlag(ValueFormat.YAdvanceDevice))
            {
                YAdvDeviceLength = reader.ReadUShort();
            }
        }
    }
}