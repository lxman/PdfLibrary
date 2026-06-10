namespace FontParser.Extensions
{
    public static class UshortExtensions
    {
        public static float ToF26Dot6(this ushort value)
        {
            return value / 64f;
        }

        public static float ToF2Dot14(this ushort value)
        {
            // F2Dot14 is a SIGNED 2.14 fixed-point value: reinterpret the 16 bits as a
            // signed short before scaling (e.g. 0xC000 = -1.0, not +3.0).
            return (short)value / 16384f;
        }
    }
}