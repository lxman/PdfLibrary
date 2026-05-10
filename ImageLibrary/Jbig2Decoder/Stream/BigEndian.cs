namespace Jbig2Decoder.Stream
{
    internal static class BigEndian
    {
        public static uint U32(byte[] data, int offset) =>
            ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];

        public static int I32(byte[] data, int offset) => (int)U32(data, offset);

        public static ushort U16(byte[] data, int offset) =>
            (ushort)((data[offset] << 8) | data[offset + 1]);
    }
}
