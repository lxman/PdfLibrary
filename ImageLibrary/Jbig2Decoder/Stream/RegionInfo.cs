namespace Jbig2Decoder.Stream
{
    /// <summary>
    /// Region segment information field (T.88 §7.4.1) — 17 bytes that prefix
    /// every region segment body (generic, text, halftone, etc).
    /// </summary>
    internal struct RegionInfo
    {
        public uint Width;
        public uint Height;
        public uint X;
        public uint Y;
        public byte Flags;

        public int ExternalCombinationOperator => Flags & 0x07;

        public static RegionInfo Parse(byte[] data, int offset)
        {
            return new RegionInfo
            {
                Width  = BigEndian.U32(data, offset),
                Height = BigEndian.U32(data, offset + 4),
                X      = BigEndian.U32(data, offset + 8),
                Y      = BigEndian.U32(data, offset + 12),
                Flags  = data[offset + 16],
            };
        }
    }
}
